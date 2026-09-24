using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// أساس خدمات الإدخال/التعديل/الحذف/القائمة للكيانات البسيطة. الخدمات المتخصصة تتجاوز نقاط
/// التوسّع (التحقق، البحث، الأحداث قبل/بعد) بدل إعادة كتابة الحلقة الأساسية.
/// </summary>
public abstract class CrudService<TEntity, TDto, TCreate, TUpdate> : ICrudService<TDto, TCreate, TUpdate>
    where TEntity : BaseEntity, new()
    where TDto : new()
    where TCreate : class
    where TUpdate : TCreate
{
    protected readonly ErpDbContext Db;

    protected CrudService(ErpDbContext db) => Db = db;

    /// <summary>اسم الكيان للرسائل (بالعربية).</summary>
    protected virtual string Label => "السجل";

    // ---------- نقاط التوسّع ----------
    protected virtual IQueryable<TEntity> ApplySearch(IQueryable<TEntity> query, string term) => query;
    protected virtual IQueryable<TEntity> ApplyFilters(IQueryable<TEntity> query, PaginationParams p) => query;
    protected virtual Task ValidateAsync(TCreate dto, TEntity? existing, CancellationToken ct) => Task.CompletedTask;
    protected virtual Task OnCreatingAsync(TEntity entity, TCreate dto, CancellationToken ct) => Task.CompletedTask;
    protected virtual Task OnUpdatingAsync(TEntity entity, TUpdate dto, CancellationToken ct) => Task.CompletedTask;
    protected virtual Task OnCreatedAsync(TEntity entity, CancellationToken ct) => Task.CompletedTask;
    protected virtual Task OnDeletingAsync(TEntity entity, CancellationToken ct) => Task.CompletedTask;
    protected virtual TDto ToDto(TEntity entity) => Mapper.Map<TDto>(entity);

    /// <summary>تُفعَّل للكيانات التي تنشئ سجلات مرتبطة (مثل حساب العميل) لتكون العملية ذرّية.</summary>
    protected virtual bool Transactional => false;

    private Task<T> Run<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
        => Transactional ? new TransactionRunner(Db).RunAsync(work, ct) : work(ct);

    protected IQueryable<TEntity> Includes(IQueryable<TEntity> query)
    {
        var et = Db.Model.FindEntityType(typeof(TEntity));
        if (et == null) return query;
        var navs = et.GetNavigations().Where(n => n.IsCollection).ToList();
        foreach (var nav in navs) query = query.Include(nav.Name);
        return navs.Count > 1 ? query.AsSplitQuery() : query;
    }

    // ---------- العمليات ----------
    public virtual async Task<PagedResult<TDto>> ListAsync(PaginationParams p, CancellationToken ct = default)
    {
        var query = Db.Set<TEntity>().AsNoTracking();
        query = ApplyFilters(query, p);
        if (!string.IsNullOrWhiteSpace(p.SearchTerm)) query = ApplySearch(query, p.SearchTerm.Trim());

        var total = await query.CountAsync(ct);

        var sortBy = ResolveSortProperty(p.SortBy);
        query = p.IsDescending
            ? query.OrderByDescending(e => EF.Property<object>(e, sortBy))
            : query.OrderBy(e => EF.Property<object>(e, sortBy));

        var entities = await Includes(query)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);

        return new PagedResult<TDto>
        {
            Items = entities.Select(ToDto).ToList(),
            TotalCount = total,
            PageNumber = p.NormalizedPage,
            PageSize = p.NormalizedSize,
        };
    }

    private string ResolveSortProperty(string? requested)
    {
        var et = Db.Model.FindEntityType(typeof(TEntity))!;
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var match = et.GetProperties().FirstOrDefault(x => string.Equals(x.Name, requested, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match.Name;
        }
        return nameof(BaseEntity.CreatedAt);
    }

    public virtual async Task<TDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await Includes(Db.Set<TEntity>().AsNoTracking()).FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new NotFoundException($"{Label} غير موجود");
        return ToDto(entity);
    }

    public virtual Task<TDto> CreateAsync(TCreate dto, CancellationToken ct = default)
        => Run(async token =>
        {
            await ValidateAsync(dto, null, token);
            var entity = Mapper.Map<TEntity>(dto);
            if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid(); // بعض الخدمات تحتاج المعرّف قبل الحفظ (حساب العميل...)
            await OnCreatingAsync(entity, dto, token);
            Db.Set<TEntity>().Add(entity);
            await SaveAsync(token);
            await OnCreatedAsync(entity, token);
            return ToDto(entity);
        }, ct);

    public virtual Task<TDto> UpdateAsync(Guid id, TUpdate dto, CancellationToken ct = default)
        => Run(async token =>
        {
            var entity = await Includes(Db.Set<TEntity>()).FirstOrDefaultAsync(e => e.Id == id, token)
                ?? throw new NotFoundException($"{Label} غير موجود");
            await ValidateAsync(dto, entity, token);
    
            Mapper.Apply(dto, entity);
            foreach (var removed in Mapper.SyncCollections(dto, entity, added => Db.Add(added)))
                Db.Remove(removed);
    
            await OnUpdatingAsync(entity, dto, token);
            await SaveAsync(token);
            return ToDto(entity);
        }, ct);

    public virtual Task DeleteAsync(Guid id, CancellationToken ct = default)
        => Run(async token =>
        {
            var entity = await Db.Set<TEntity>().FirstOrDefaultAsync(e => e.Id == id, token)
                ?? throw new NotFoundException($"{Label} غير موجود");
            await OnDeletingAsync(entity, token);
            Db.Remove(entity);
            await SaveAsync(token);
            return true;
        }, ct);

    protected async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsConstraintViolation(ex))
        {
            throw new ConflictException($"تعذّر الحفظ: {Label} مرتبط بسجلات أخرى أو يخالف قيد تفرّد.");
        }
    }

    private static bool IsConstraintViolation(DbUpdateException ex)
    {
        var text = ex.InnerException?.Message ?? ex.Message;
        return text.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase)
            || text.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
            || text.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
            || text.Contains("REFERENCE constraint", StringComparison.OrdinalIgnoreCase);
    }
}
