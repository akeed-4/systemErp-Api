using ERP.Core.Contracts.Shared;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// مستند تحويل المخزون: يتحقق من المستودعين والأصناف ثم ينقل كل سطر عبر محرك المخزون في معاملة واحدة.
/// لا قيد محاسبي له: حساب المخزون واحد والتكلفة موحّدة، فالتحويل لا يغيّر قيمة المخزون.
/// </summary>
public class StockTransferService : IStockTransferService
{
    private readonly ErpDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly INumberSequenceService _numbers;
    private readonly ITransactionRunner _tx;
    private readonly IAuditService _audit;

    public StockTransferService(ErpDbContext db, IInventoryService inventory, INumberSequenceService numbers, ITransactionRunner tx, IAuditService audit)
    {
        _db = db; _inventory = inventory; _numbers = numbers; _tx = tx; _audit = audit;
    }

    public Task<StockTransferDto> CreateAsync(CreateStockTransferDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            if (r.FromWarehouseId == r.ToWarehouseId) throw new ValidationFailedException(Messages.TransferWarehousesMustDiffer);
            if (r.Items.Count == 0 || r.Items.Any(i => i.Quantity <= 0)) throw new ValidationFailedException(Messages.TransferRequiresItems);

            var from = await WarehouseStocks.ResolveAsync(_db, r.FromWarehouseId, token);
            var to = await WarehouseStocks.ResolveAsync(_db, r.ToWarehouseId, token);
            if (to.Status != "active") throw new ValidationFailedException(Messages.WarehouseInactive);

            var ids = r.Items.Select(i => i.ItemId).Distinct().ToList();
            var products = await _db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, token);

            var transfer = new StockTransfer
            {
                TransferNumber = await _numbers.NextAsync("stock_transfer", "TRF-", token),
                Date = r.Date ?? DateTime.UtcNow, Notes = r.Notes,
                FromWarehouseId = from.Id, FromWarehouseName = from.NameAr, ToWarehouseId = to.Id, ToWarehouseName = to.NameAr,
            };
            // سطر واحد لكل صنف: تكرار الصنف يُجمع
            foreach (var line in r.Items.GroupBy(i => i.ItemId))
            {
                var product = products.GetValueOrDefault(line.Key) ?? throw new NotFoundException(Messages.ProductNotFound);
                transfer.Items.Add(new StockTransferItem
                {
                    ItemId = product.Id, Sku = product.Sku, ItemName = product.NameAr, Unit = product.Unit, Quantity = line.Sum(i => i.Quantity),
                });
            }
            _db.Add(transfer);
            await _db.SaveChangesAsync(token);

            foreach (var item in transfer.Items)
                await _inventory.TransferAsync(new TransferStockDto
                {
                    ItemId = item.ItemId, FromWarehouseId = from.Id, ToWarehouseId = to.Id, Quantity = item.Quantity, Date = transfer.Date,
                    ReferenceNumber = transfer.TransferNumber, SourceId = transfer.Id, Notes = $"تحويل من {from.NameAr} إلى {to.NameAr}",
                }, token);

            await _audit.LogAsync("create", nameof(StockTransfer), transfer.Id.ToString(), $"{transfer.TransferNumber}: {from.NameAr} ← {to.NameAr}", token);
            return Map(transfer);
        }, ct);

    public async Task<StockTransferDto> GetAsync(Guid id, CancellationToken ct = default)
        => Map(await _db.Set<StockTransfer>().AsNoTracking().Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(Messages.StockTransferNotFound));

    public async Task<PagedResult<StockTransferDto>> ListAsync(PaginationParams p, CancellationToken ct = default)
    {
        var q = _db.Set<StockTransfer>().AsNoTracking().AsQueryable();
        if (p.StartDate.HasValue) q = q.Where(t => t.Date >= p.StartDate);
        if (p.EndDate.HasValue) q = q.Where(t => t.Date <= p.EndDate);
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var term = p.SearchTerm.Trim();
            q = q.Where(t => t.TransferNumber.Contains(term) || t.FromWarehouseName.Contains(term) || t.ToWarehouseName.Contains(term));
        }
        var total = await q.CountAsync(ct);
        var items = await q.Include(t => t.Items).OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);
        return new PagedResult<StockTransferDto>
        {
            Items = items.Select(Map).ToList(), TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    private static StockTransferDto Map(StockTransfer t) => new()
    {
        Id = t.Id, CreatedAt = t.CreatedAt, TransferNumber = t.TransferNumber, Date = t.Date, Notes = t.Notes,
        FromWarehouseId = t.FromWarehouseId, FromWarehouseName = t.FromWarehouseName, ToWarehouseId = t.ToWarehouseId, ToWarehouseName = t.ToWarehouseName,
        Items = t.Items.OrderBy(i => i.Sku).Select(i => new StockTransferItemDto { ItemId = i.ItemId, Sku = i.Sku, ItemName = i.ItemName, Unit = i.Unit, Quantity = i.Quantity }).ToList(),
    };
}
