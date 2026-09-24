using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace ERP.Service.Services.Shared;

/// <summary>
/// ناسخ خصائص بالاسم بين الكيانات والـ DTOs (بما فيها القوائم الفرعية والكائنات المملوكة).
/// تُنسخ فقط الخصائص الموجودة في الطرفين، فلا يتسرّب أي حقل غير موجود في الـ DTO.
/// </summary>
public static class Mapper
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Props = new();

    private static PropertyInfo[] PropsOf(Type t) => Props.GetOrAdd(t,
        x => x.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.CanWrite).ToArray());

    public static T Map<T>(object source) where T : new() => (T)Map(source, typeof(T));

    public static object Map(object source, Type targetType)
    {
        var target = Activator.CreateInstance(targetType)!;
        CopyScalars(source, target);
        CopyCollections(source, target);
        return target;
    }

    /// <summary>ينسخ القيم البسيطة والكائنات المملوكة فقط (لا القوائم) من المصدر إلى الهدف الموجود.</summary>
    public static void Apply(object source, object target) => CopyScalars(source, target);

    private static bool IsScalar(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime)
            || t == typeof(Guid) || t == typeof(DateTimeOffset) || t == typeof(TimeSpan) || t == typeof(byte[]);
    }

    private static bool IsCollection(Type t) => t != typeof(string) && typeof(IEnumerable).IsAssignableFrom(t);

    private static void CopyScalars(object source, object target)
    {
        var sourceProps = PropsOf(source.GetType()).ToDictionary(p => p.Name);
        foreach (var tp in PropsOf(target.GetType()))
        {
            if (!sourceProps.TryGetValue(tp.Name, out var sp)) continue;
            if (IsCollection(tp.PropertyType) || IsCollection(sp.PropertyType)) continue;
            var value = sp.GetValue(source);

            if (IsScalar(tp.PropertyType) && IsScalar(sp.PropertyType))
            {
                if (tp.Name == "Id" && value is Guid g && g == Guid.Empty) continue; // اترك المعرّف المولَّد للكيان
                var tt = Nullable.GetUnderlyingType(tp.PropertyType) ?? tp.PropertyType;
                var st = Nullable.GetUnderlyingType(sp.PropertyType) ?? sp.PropertyType;
                if (tt != st) continue;
                if (value == null && tp.PropertyType.IsValueType && Nullable.GetUnderlyingType(tp.PropertyType) == null) continue;
                tp.SetValue(target, value);
            }
            else if (!IsScalar(tp.PropertyType) && !IsScalar(sp.PropertyType) && value != null)
            {
                var existing = tp.GetValue(target);
                if (existing == null)
                {
                    existing = Activator.CreateInstance(tp.PropertyType)!;
                    tp.SetValue(target, existing);
                }
                CopyScalars(value, existing);
            }
        }
    }

    /// <summary>ينشئ قوائم فرعية جديدة في الهدف من القوائم المطابقة في المصدر.</summary>
    public static void CopyCollections(object source, object target)
    {
        var sourceProps = PropsOf(source.GetType()).ToDictionary(p => p.Name);
        foreach (var tp in PropsOf(target.GetType()))
        {
            if (!IsCollection(tp.PropertyType) || !sourceProps.TryGetValue(tp.Name, out var sp)) continue;
            if (!IsCollection(sp.PropertyType) || sp.GetValue(source) is not IEnumerable items) continue;
            var elementType = tp.PropertyType.GetGenericArguments().FirstOrDefault();
            if (elementType == null || IsScalar(elementType)) continue;

            var listType = typeof(List<>).MakeGenericType(elementType);
            var list = (IList)Activator.CreateInstance(listType)!;
            foreach (var item in items) list.Add(Map(item, elementType));
            if (tp.PropertyType.IsAssignableFrom(listType)) tp.SetValue(target, list);
        }
    }

    /// <summary>
    /// يزامن القوائم الفرعية المُتتبَّعة في الكيان مع قوائم الـ DTO: يحدّث العناصر ذات المعرّف المطابق،
    /// يضيف الجديد (ويستدعي onAdded)، ويحذف الغائب ويُرجعه ليزيله الـ DbContext.
    /// </summary>
    public static List<object> SyncCollections(object dto, object entity, Action<object> onAdded)
    {
        var removed = new List<object>();
        var dtoProps = PropsOf(dto.GetType()).ToDictionary(p => p.Name);
        foreach (var ep in PropsOf(entity.GetType()))
        {
            if (!IsCollection(ep.PropertyType) || !dtoProps.TryGetValue(ep.Name, out var dp)) continue;
            if (dp.GetValue(dto) is not IEnumerable dtoItems) continue;
            var elementType = ep.PropertyType.GetGenericArguments().FirstOrDefault();
            if (elementType == null || IsScalar(elementType)) continue;

            var current = ep.GetValue(entity) as IList;
            if (current == null)
            {
                current = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
                ep.SetValue(entity, current);
            }

            var idProp = elementType.GetProperty("Id")!;
            var existingById = current.Cast<object>().ToDictionary(o => (Guid)idProp.GetValue(o)!, o => o);
            var keep = new HashSet<object>();

            foreach (var item in dtoItems)
            {
                var dtoId = (Guid?)item.GetType().GetProperty("Id")?.GetValue(item) ?? Guid.Empty;
                if (dtoId != Guid.Empty && existingById.TryGetValue(dtoId, out var match))
                {
                    CopyScalars(item, match);
                    keep.Add(match);
                }
                else
                {
                    var created = Map(item, elementType);
                    current.Add(created);
                    onAdded(created);
                }
            }
            foreach (var old in existingById.Values.Where(o => !keep.Contains(o)).ToList())
            {
                current.Remove(old);
                removed.Add(old);
            }
        }
        return removed;
    }
}
