using System.Linq.Expressions;
using System.Text;
using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace ERP.Service.Services.Shared;

/// <summary>
/// الفحص المركزي لمنع تكرار تعريف الكيانات (الأسماء، ورقم هوية الموظف). المصدر الوحيد لقواعد التكرار والتطبيع:
/// <list type="bullet">
/// <item>يُستدعى من <see cref="CrudService{TEntity,TDto,TCreate,TUpdate}"/> لكل إنشاء وتعديل، ومن المسارات التي تكتب اسماً أدخله المستخدم على كيان آخر
/// (حساب جديد ينشئ عميله/مورده/بنكه، وتعديل اسم الحساب ينقل اسمه لكيانه). الأسماء التي يولّدها النظام آلياً لا تمر به.</item>
/// <item>التطبيع: قص الأطراف، ومسافة واحدة بين الكلمات، وإزالة التطويل والتشكيل، وأحرف صغيرة. لا توحيد للهمزات ولا التاء المربوطة ولا الألف المقصورة،
/// ولا يُعد التشابه الجزئي تطابقاً.</item>
/// <item>كل حقل يُفحص وحده (العربي مع العربي، والإنجليزي مع الإنجليزي)، والفارغ لا يُفحص.</item>
/// <item>النطاق: المنشأة دائماً (مرشّح المنشأة العام)، وبعض الكيانات أضيق: الحساب داخل أبيه، والمركز داخل أبيه، والبنك بعملته.</item>
/// <item>السجلات الموقوفة محسوبة، والسجل نفسه مستثنى عند التعديل، والتعديل الذي لا يغيّر القيمة المطبَّعة ولا النطاق لا يُفحص
/// (فتكرار قديم لا يمنع تعديل بقية الحقول).</item>
/// </list>
/// إضافة كيان جديد = سطر في <see cref="Rules"/>.
/// </summary>
public static class DuplicateEntityValidator
{
    private static readonly Field NameAr = new("NameAr", () => Messages.DuplicateFieldNameAr, NormalizeName);
    private static readonly Field NameEn = new("NameEn", () => Messages.DuplicateFieldNameEn, NormalizeName);
    private static readonly Field NationalId = new(nameof(Employee.NationalId), () => Messages.DuplicateFieldIdNumber, NormalizeIdentifier);

    private static readonly Dictionary<Type, Rule> Rules = new Rule[]
    {
        new Rule<Account>(() => Messages.LabelAccount, [NameAr, NameEn], nameof(Account.ParentCode)),
        new Rule<Customer>(() => Messages.PartyCustomer, [NameAr, NameEn]),
        new Rule<Supplier>(() => Messages.PartySupplier, [NameAr, NameEn]),
        new Rule<BankEntity>(() => Messages.LabelBank, [NameAr, NameEn], nameof(BankEntity.Currency)),
        new Rule<PaymentMethodItem>(() => Messages.LabelPaymentMethod, [NameAr, NameEn]),
        new Rule<CostCenter>(() => Messages.LabelCostCenter, [NameAr, NameEn], nameof(CostCenter.ParentId)),
        new Rule<Warehouse>(() => Messages.LabelWarehouse, [NameAr, NameEn]),
        new Rule<Branch>(() => Messages.LabelBranch, [NameAr, NameEn]),
        // الموظفون: رقم الهوية لا الاسم (تطابق أسماء الأشخاص مشروع)
        new Rule<Employee>(() => Messages.LabelEmployee, [NationalId]),
    }.ToDictionary(r => r.EntityType);

    /// <summary>
    /// يرفض (409) حفظ الكيان إن طابقت إحدى قيمه المفحوصة سجلاً آخر في نطاقه. الكيان متتبَّع (مضاف أو معدَّل) أو جديد غير متتبَّع؛
    /// والكيان الذي لا قاعدة له يمر بلا فحص.
    /// </summary>
    public static Task EnsureUniqueAsync(ErpDbContext db, object entity, CancellationToken ct)
        => Rules.TryGetValue(entity.GetType(), out var rule) ? rule.CheckAsync(db, entity, ct) : Task.CompletedTask;

    /// <summary>مفتاح مقارنة الاسم، أو null للفارغ.</summary>
    public static string? NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var sb = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var ch in value)
        {
            if (ch == Tatweel || IsTashkeel(ch)) continue;
            if (char.IsWhiteSpace(ch)) { pendingSpace = sb.Length > 0; continue; }
            if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    /// <summary>مفتاح مقارنة المعرّف (رقم الهوية): بلا مسافات وبأحرف كبيرة، أو null للفارغ.</summary>
    public static string? NormalizeIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var key = new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        return key.Length == 0 ? null : key;
    }

    private const char Tatweel = 'ـ';

    /// <summary>الحركات والتنوين والشدة والسكون (U+064B–U+0652) والألف الخنجرية (U+0670).</summary>
    private static bool IsTashkeel(char ch) => ch is >= 'ً' and <= 'ْ' or 'ٰ';

    // ---------- القواعد ----------

    private sealed record Field(string Property, Func<string> Label, Func<string?, string?> Normalize);

    /// <summary>سجل مرشّح للمقارنة: معرّفه ورمزه وقيمتا الحقلين المفحوصين (الثانية فارغة لقاعدة بحقل واحد).</summary>
    private sealed record Candidate(Guid Id, string Code, string? First, string? Second);

    private abstract class Rule
    {
        public abstract Type EntityType { get; }
        public abstract Task CheckAsync(ErpDbContext db, object entity, CancellationToken ct);
    }

    private sealed class Rule<T> : Rule where T : BaseEntity
    {
        private readonly Func<string> _label;
        private readonly Field[] _fields;
        private readonly string? _scope;
        private readonly Expression<Func<T, Candidate>> _project;

        public Rule(Func<string> label, Field[] fields, string? scope = null)
        {
            if (fields.Length is < 1 or > 2) throw new ArgumentException("A duplicate rule checks one or two fields.", nameof(fields));
            _label = label;
            _fields = fields;
            _scope = scope;
            var x = Expression.Parameter(typeof(T), "x");
            Expression Text(string property) => Expression.Property(x, property);
            _project = Expression.Lambda<Func<T, Candidate>>(
                Expression.New(typeof(Candidate).GetConstructors().Single(),
                    Expression.Property(x, nameof(BaseEntity.Id)),
                    Text("Code"),
                    Text(fields[0].Property),
                    fields.Length > 1 ? Text(fields[1].Property) : Expression.Constant(null, typeof(string))),
                x);
        }

        public override Type EntityType => typeof(T);

        public override async Task CheckAsync(ErpDbContext db, object entity, CancellationToken ct)
        {
            var e = (T)entity;
            var entry = db.Entry(e);
            var isNew = entry.State is EntityState.Added or EntityState.Detached;
            var scopeChanged = !isNew && _scope != null && !Equals(entry.OriginalValues[_scope], entry.CurrentValues[_scope]);

            var pending = new List<(int Index, string Key, string Value)>();
            for (var i = 0; i < _fields.Length; i++)
            {
                var f = _fields[i];
                var value = entry.CurrentValues[f.Property] as string;
                if (f.Normalize(value) is not { } key) continue;
                if (!isNew && !scopeChanged && f.Normalize(entry.OriginalValues[f.Property] as string) == key) continue;
                pending.Add((i, key, value!.Trim()));
            }
            if (pending.Count == 0) return;

            var query = db.Set<T>().AsNoTracking().Where(o => o.Id != e.Id);
            if (_scope != null) query = query.Where(SameScope(entry.CurrentValues[_scope]));
            var candidates = await query.Select(_project).ToListAsync(ct);

            foreach (var (index, key, value) in pending)
            {
                var f = _fields[index];
                var hit = candidates.FirstOrDefault(c => f.Normalize(index == 0 ? c.First : c.Second) == key);
                if (hit != null)
                    throw new ConflictException(string.Format(Messages.DuplicateEntityValue, f.Label(), value, _label(), hit.Code));
            }
        }

        /// <summary>o => o.[النطاق] == القيمة (قيمة معاملة لا ثابتاً في SQL، وnull يطابق null).</summary>
        private Expression<Func<T, bool>> SameScope(object? value)
        {
            var o = Expression.Parameter(typeof(T), "o");
            var member = Expression.Property(o, _scope!);
            var holder = Expression.Property(Expression.Constant(new ScopeValue(value)), nameof(ScopeValue.Value));
            return Expression.Lambda<Func<T, bool>>(Expression.Equal(member, Expression.Convert(holder, member.Type)), o);
        }
    }

    private sealed class ScopeValue(object? value)
    {
        public object? Value { get; } = value;
    }
}
