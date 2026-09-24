using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.BuildingBlocks.Web;
using Erp.Modules.Inventory.Domain;
using Erp.Modules.Inventory.Persistence;
using Erp.Modules.Organization.Contracts;
using Erp.SharedKernel.Errors;
using Erp.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Inventory.Endpoints;

internal sealed record CategoryDto(Guid Id, string Code, string NameAr, string NameEn, Guid? ParentId, string? Description, int ItemCount);

internal sealed record SaveCategoryRequest(string? Code, string NameAr, string? NameEn, Guid? ParentId, string? Description);

internal sealed record UnitDto(Guid Id, string Code, string NameAr, string NameEn, string Symbol, bool IsBaseUnit, Guid? BaseUnitId, decimal ConversionFactor, string Status);

internal sealed record SaveUnitRequest(string? Code, string NameAr, string? NameEn, string? Symbol, Guid? BaseUnitId, decimal? ConversionFactor, string? Status);

internal sealed record WarehouseDto(Guid Id, Guid TenantId, string Code, string NameAr, string NameEn, string? Location, Guid? BranchId, string? ManagerName, string? Phone, bool IsDefault, string Status);

internal sealed record SaveWarehouseRequest(string? Code, string NameAr, string? NameEn, string? Location, Guid? BranchId, string? ManagerName, string? Phone, bool? IsDefault, string? Status);

/// <summary>master-data/categories, master-data/units and warehouses.</summary>
internal static class InventorySetupEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var categories = app.MapGroup("/api/v1/product-categories").WithTags("Inventory");
        categories.MapGet(string.Empty, ListCategoriesAsync).RequireAuthorization();
        categories.MapPost(string.Empty, CreateCategoryAsync).RequireScreen(ScreenIds.MasterData, ScreenAction.Create);
        categories.MapPut("{id:guid}", UpdateCategoryAsync).RequireScreen(ScreenIds.MasterData, ScreenAction.Edit);
        categories.MapDelete("{id:guid}", DeleteCategoryAsync).RequireScreen(ScreenIds.MasterData, ScreenAction.Delete);

        var units = app.MapGroup("/api/v1/units").WithTags("Inventory");
        units.MapGet(string.Empty, ListUnitsAsync).RequireAuthorization();
        units.MapPost(string.Empty, CreateUnitAsync).RequireScreen(ScreenIds.MasterData, ScreenAction.Create);
        units.MapPut("{id:guid}", UpdateUnitAsync).RequireScreen(ScreenIds.MasterData, ScreenAction.Edit);
        units.MapDelete("{id:guid}", DeleteUnitAsync).RequireScreen(ScreenIds.MasterData, ScreenAction.Delete);

        var warehouses = app.MapGroup("/api/v1/warehouses").WithTags("Inventory");
        warehouses.MapGet(string.Empty, ListWarehousesAsync).RequireAuthorization();
        warehouses.MapPost(string.Empty, CreateWarehouseAsync).RequireScreen(ScreenIds.MasterData, ScreenAction.Create);
        warehouses.MapPut("{id:guid}", UpdateWarehouseAsync).RequireScreen(ScreenIds.MasterData, ScreenAction.Edit);
    }

    private static async Task<IResult> ListCategoriesAsync(InventoryDbContext db, CancellationToken ct) =>
        ErpResults.Ok(await db.Categories.AsNoTracking().OrderBy(c => c.Code)
            .Select(c => new CategoryDto(c.Id, c.Code, c.NameAr, c.NameEn, c.ParentId, c.Description, db.Products.Count(p => p.CategoryId == c.Id)))
            .ToListAsync(ct));

    private static async Task<IResult> CreateCategoryAsync(SaveCategoryRequest request, InventoryDbContext db, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var code = RequireCode(request.Code, request.NameAr);
        if (await db.Categories.AnyAsync(c => c.Code == code, ct))
        {
            throw ErpException.Conflict("category_code_taken", $"Category {code} already exists.", $"التصنيف {code} موجود مسبقاً.");
        }

        var category = new ProductCategory(code);
        category.Update(request.NameAr, request.NameEn ?? string.Empty, request.ParentId, request.Description);
        db.Categories.Add(category);
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Created($"/api/v1/product-categories/{category.Id}", new CategoryDto(category.Id, category.Code, category.NameAr, category.NameEn, category.ParentId, category.Description, 0), "تم إضافة التصنيف");
    }

    private static async Task<IResult> UpdateCategoryAsync(Guid id, SaveCategoryRequest request, InventoryDbContext db, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw ErpException.NotFound("Category", "التصنيف");
        if (request.ParentId == id)
        {
            throw ErpException.Validation("A category cannot be its own parent.", "لا يمكن أن يكون التصنيف أباً لنفسه.");
        }

        category.Update(request.NameAr, request.NameEn ?? string.Empty, request.ParentId, request.Description);
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Ok(new CategoryDto(category.Id, category.Code, category.NameAr, category.NameEn, category.ParentId, category.Description, await db.Products.CountAsync(p => p.CategoryId == id, ct)), "تم تحديث التصنيف");
    }

    private static async Task<IResult> DeleteCategoryAsync(Guid id, InventoryDbContext db, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw ErpException.NotFound("Category", "التصنيف");
        var inUse = ErpException.Conflict("category_in_use", "The category has products or sub-categories.", "التصنيف مستخدم في أصناف أو تصنيفات فرعية.");
        if (await db.Products.AnyAsync(p => p.CategoryId == id, ct) || await db.Categories.AnyAsync(c => c.ParentId == id, ct))
        {
            throw inUse;
        }

        db.Categories.Remove(category);
        await SaveOrConflictAsync(unitOfWork, inUse, ct);
        return ErpResults.Ok(new { id }, "تم حذف التصنيف");
    }

    private static async Task<IResult> ListUnitsAsync(InventoryDbContext db, CancellationToken ct) =>
        ErpResults.Ok(await db.Units.AsNoTracking().OrderBy(u => u.Code).Select(u => ToDto(u)).ToListAsync(ct));

    private static async Task<IResult> CreateUnitAsync(SaveUnitRequest request, InventoryDbContext db, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var code = RequireCode(request.Code, request.NameAr);
        if (await db.Units.AnyAsync(u => u.Code == code, ct))
        {
            throw ErpException.Conflict("unit_code_taken", $"Unit {code} already exists.", $"الوحدة {code} موجودة مسبقاً.");
        }

        var unit = new UnitOfMeasure(code);
        await ApplyUnitAsync(unit, request, db, ct);
        db.Units.Add(unit);
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Created($"/api/v1/units/{unit.Id}", ToDto(unit), "تم إضافة الوحدة");
    }

    private static async Task<IResult> UpdateUnitAsync(Guid id, SaveUnitRequest request, InventoryDbContext db, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var unit = await db.Units.SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw ErpException.NotFound("Unit", "الوحدة");
        await ApplyUnitAsync(unit, request, db, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Ok(ToDto(unit), "تم تحديث الوحدة");
    }

    private static async Task<IResult> DeleteUnitAsync(Guid id, InventoryDbContext db, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var unit = await db.Units.SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw ErpException.NotFound("Unit", "الوحدة");
        var inUse = ErpException.Conflict("unit_in_use", "The unit is used by products or other units.", "الوحدة مستخدمة في أصناف أو وحدات أخرى.");
        if (await db.Products.AnyAsync(p => p.UnitId == id, ct) || await db.Units.AnyAsync(u => u.BaseUnitId == id, ct))
        {
            throw inUse;
        }

        db.Units.Remove(unit);
        await SaveOrConflictAsync(unitOfWork, inUse, ct);
        return ErpResults.Ok(new { id }, "تم حذف الوحدة");
    }

    private static async Task<IResult> ListWarehousesAsync(InventoryDbContext db, CancellationToken ct) =>
        ErpResults.Ok(await db.Warehouses.AsNoTracking().OrderByDescending(w => w.IsDefault).ThenBy(w => w.Code).Select(w => ToDto(w)).ToListAsync(ct));

    private static async Task<IResult> CreateWarehouseAsync(SaveWarehouseRequest request, InventoryDbContext db, IBranchDirectory branches, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var code = RequireCode(request.Code, request.NameAr);
        if (await db.Warehouses.AnyAsync(w => w.Code == code, ct))
        {
            throw ErpException.Conflict("warehouse_code_taken", $"Warehouse {code} already exists.", $"المستودع {code} موجود مسبقاً.");
        }

        var warehouse = new Warehouse(code);
        await ApplyWarehouseAsync(warehouse, request, db, branches, ct);
        db.Warehouses.Add(warehouse);
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Created($"/api/v1/warehouses/{warehouse.Id}", ToDto(warehouse), "تم إضافة المستودع");
    }

    private static async Task<IResult> UpdateWarehouseAsync(Guid id, SaveWarehouseRequest request, InventoryDbContext db, IBranchDirectory branches, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var warehouse = await db.Warehouses.SingleOrDefaultAsync(w => w.Id == id, ct) ?? throw ErpException.NotFound("Warehouse", "المستودع");
        await ApplyWarehouseAsync(warehouse, request, db, branches, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Ok(ToDto(warehouse), "تم تحديث المستودع");
    }

    private static async Task ApplyUnitAsync(UnitOfMeasure unit, SaveUnitRequest request, InventoryDbContext db, CancellationToken ct)
    {
        if (request.BaseUnitId is { } baseId && (baseId == unit.Id || !await db.Units.AnyAsync(u => u.Id == baseId && u.BaseUnitId == null, ct)))
        {
            throw ErpException.Validation("The base unit must be an existing base unit.", "الوحدة الأساسية يجب أن تكون وحدة أساسية موجودة.");
        }

        if (request.BaseUnitId is not null && request.ConversionFactor is not > 0)
        {
            throw ErpException.Validation("A positive conversion factor is required.", "معامل التحويل يجب أن يكون أكبر من صفر.");
        }

        unit.Update(request.NameAr, request.NameEn ?? string.Empty, request.Symbol ?? request.NameAr, request.BaseUnitId, request.ConversionFactor ?? 1,
            !string.Equals(request.Status, "inactive", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task ApplyWarehouseAsync(Warehouse warehouse, SaveWarehouseRequest request, InventoryDbContext db, IBranchDirectory branches, CancellationToken ct)
    {
        if (request.BranchId is { } branchId && await branches.FindAsync(branchId, ct) is null)
        {
            throw ErpException.Validation("Unknown branch.", "الفرع غير موجود.");
        }

        warehouse.Update(request.NameAr, request.NameEn ?? string.Empty, request.Location, request.BranchId, request.ManagerName, request.Phone,
            !string.Equals(request.Status, "inactive", StringComparison.OrdinalIgnoreCase));

        if (request.IsDefault == true && !warehouse.IsDefault)
        {
            // Only one default warehouse per tenant (filtered unique index): clear the old one first.
            await db.Warehouses.Where(w => w.IsDefault).ExecuteUpdateAsync(s => s.SetProperty(w => w.IsDefault, false), ct);
            warehouse.SetDefault(true);
        }
    }

    /// <summary>Soft-deleted products still reference their category/unit; the FK then refuses the delete.</summary>
    private static async Task SaveOrConflictAsync(IUnitOfWork unitOfWork, ErpException conflict, CancellationToken ct)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw conflict;
        }
    }

    private static string RequireCode(string? code, string? nameAr)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(nameAr))
        {
            throw ErpException.Validation("Code and Arabic name are required.", "الرمز والاسم بالعربي مطلوبان.");
        }

        return code.Trim().ToUpperInvariant();
    }

    private static UnitDto ToDto(UnitOfMeasure u) =>
        new(u.Id, u.Code, u.NameAr, u.NameEn, u.Symbol, u.IsBaseUnit, u.BaseUnitId, u.ConversionFactor, u.IsActive ? "active" : "inactive");

    private static WarehouseDto ToDto(Warehouse w) =>
        new(w.Id, w.TenantId, w.Code, w.NameAr, w.NameEn, w.Location, w.BranchId, w.ManagerName, w.Phone, w.IsDefault, w.IsActive ? "active" : "inactive");
}
