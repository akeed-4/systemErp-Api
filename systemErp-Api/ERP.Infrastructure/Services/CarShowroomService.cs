using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public class CarShowroomService : ICarShowroomService
{
    private readonly IApplicationDbContext _context;
    private readonly IAccountingService _accountingService;

    public CarShowroomService(IApplicationDbContext context, IAccountingService accountingService)
    {
        _context = context;
        _accountingService = accountingService;
    }

    public async Task ProcessProcurementStageTransitionAsync(Guid orderId, string nextStage, CancellationToken ct = default)
    {
        var order = await _context.CarProcurementOrders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order == null || !Enum.TryParse<ProcurementStage>(nextStage, true, out var stage)) return;

        var status = stage switch
        {
            ProcurementStage.RequisitionApproved or ProcurementStage.RfqApproved => ProcurementOrderStatus.Approved,
            ProcurementStage.PurchaseOrder => ProcurementOrderStatus.InProgress,
            ProcurementStage.VinReceived => ProcurementOrderStatus.Received,
            ProcurementStage.Invoiced => ProcurementOrderStatus.Invoiced,
            _ => order.Status,
        };

        order.UpdateStageAndStatus(stage, status);
        await _context.SaveChangesAsync(ct);

        if (stage == ProcurementStage.VinReceived)
        {
            await SyncVehiclesFromProcurementOrderAsync(orderId, ct);
        }
        else if (stage == ProcurementStage.Invoiced)
        {
            await GeneratePurchaseInvoiceFromProcurementAsync(orderId, ct);
        }
    }

    public async Task SyncVehiclesFromProcurementOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await _context.CarProcurementOrders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order == null) return;

        foreach (var item in order.Items)
        {
            if (string.IsNullOrEmpty(item.AssignedVins)) continue;

            foreach (var rawVin in item.AssignedVins.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var vin = rawVin.Trim();
                if (await _context.Vehicles.AnyAsync(v => v.ChassisNumber == vin, ct)) continue;

                _context.Vehicles.Add(new Vehicle
                {
                    ChassisNumber = vin,
                    BrandNameAr = item.BrandName,
                    ModelNameAr = item.ModelName,
                    TrimNameAr = item.TrimName,
                    Year = item.Year,
                    ColorExterior = "غير محدد",
                    ColorInterior = "غير محدد",
                    Condition = VehicleCondition.New,
                    FuelType = FuelType.Petrol,
                    Transmission = TransmissionType.Automatic,
                    AgentNameAr = order.SupplierName,
                    PurchasePrice = item.UnitPrice,
                    TotalCost = item.UnitPrice,
                    SellingPrice = Math.Round(item.UnitPrice * 1.1m, 2),
                    VatAmount = Math.Round(item.UnitPrice * 0.15m, 2),
                    PriceWithVat = Math.Round(item.UnitPrice * 1.15m, 2),
                    VatMode = VatMode.Standard15,
                    Status = VehicleStatus.Available,
                    Location = "المستودع الرئيسي",
                });
            }
        }

        await _context.SaveChangesAsync(ct);
    }

    private async Task GeneratePurchaseInvoiceFromProcurementAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await _context.CarProcurementOrders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order == null) return;

        var alreadyInvoiced = await _context.Invoices
            .AnyAsync(i => i.ReferenceId == orderId && i.InvoiceType == InvoiceType.PurchaseInvoice, ct);
        if (alreadyInvoiced) return;

        var invoice = new Invoice
        {
            InvoiceNumber = $"PINV-AUTO-{order.OrderNumber}",
            InvoiceType = InvoiceType.PurchaseInvoice,
            IssueDate = DateTime.UtcNow,
            IssueTime = DateTime.UtcNow.ToString("HH:mm:ss"),
            PartyName = order.SupplierName,
            Subtotal = order.Subtotal,
            VatTotal = order.VatTotal,
            GrandTotal = order.GrandTotal,
            Status = "Posted",
            Notes = $"فاتورة مشتريات تلقائية لأمر التوريد {order.OrderNumber}",
            ReferenceType = "CarProcurementOrder",
            ReferenceId = order.Id,
            ReferenceNumber = order.OrderNumber,
        };

        foreach (var item in order.Items)
        {
            invoice.Items.Add(new InvoiceItem
            {
                ItemId = Guid.Empty,
                ItemName = $"{item.BrandName} {item.ModelName} {item.Year}",
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                VatAmount = Math.Round(item.Total * 0.15m, 2),
                TotalBeforeVat = item.Total,
                TotalAfterVat = Math.Round(item.Total * 1.15m, 2),
            });
        }

        _context.Invoices.Add(invoice);
        await _context.SaveChangesAsync(ct);

        await _accountingService.PostAutomaticInvoiceJournalAsync(invoice, ct);
    }

    public async Task ProcessSalesContractStatusTransitionAsync(Guid contractId, string nextStatus, CancellationToken ct = default)
    {
        var contract = await _context.CarSalesContracts.FirstOrDefaultAsync(c => c.Id == contractId, ct);
        if (contract == null || !Enum.TryParse<SalesContractStatus>(nextStatus, true, out var status)) return;

        contract.UpdateStatus(status);

        if (status == SalesContractStatus.Allocated)
        {
            var vehicle = await _context.Vehicles.FirstOrDefaultAsync(v => v.Id == contract.VehicleId, ct);
            vehicle?.ChangeStatus(VehicleStatus.Reserved);
        }

        await _context.SaveChangesAsync(ct);

        if (status == SalesContractStatus.Invoiced)
        {
            await GenerateInvoiceFromContractAsync(contractId, ct);
        }
    }

    public async Task GenerateInvoiceFromContractAsync(Guid contractId, CancellationToken ct = default)
    {
        var contract = await _context.CarSalesContracts.FirstOrDefaultAsync(c => c.Id == contractId, ct);
        if (contract == null) return;

        var alreadyInvoiced = await _context.Invoices.AnyAsync(i => i.ReferenceId == contractId, ct);
        if (alreadyInvoiced) return;

        var invoiceType = contract.BuyerType == BuyerType.Corporate ? InvoiceType.StandardTaxInvoice : InvoiceType.SimplifiedTaxInvoice;

        var invoice = new Invoice
        {
            InvoiceNumber = $"SINV-AUTO-{contract.ContractNumber}",
            InvoiceType = invoiceType,
            IssueDate = DateTime.UtcNow,
            IssueTime = DateTime.UtcNow.ToString("HH:mm:ss"),
            PartyName = contract.BuyerName,
            Subtotal = contract.SellingPrice,
            VatTotal = contract.VatAmount,
            GrandTotal = contract.TotalWithVat,
            Status = "Posted",
            Notes = $"فاتورة مبيعات سيارة - عقد {contract.ContractNumber}",
            ReferenceType = "CarSalesContract",
            ReferenceId = contract.Id,
            ReferenceNumber = contract.ContractNumber,
        };

        invoice.Items.Add(new InvoiceItem
        {
            ItemId = contract.VehicleId,
            ItemName = $"{contract.VehicleDescription} (VIN: {contract.Vin})",
            Quantity = 1,
            UnitPrice = contract.SellingPrice,
            UnitCost = contract.CostPrice,
            VatAmount = contract.VatAmount,
            TotalBeforeVat = contract.SellingPrice,
            TotalAfterVat = contract.TotalWithVat,
        });

        _context.Invoices.Add(invoice);

        var vehicle = await _context.Vehicles.FirstOrDefaultAsync(v => v.Id == contract.VehicleId, ct);
        vehicle?.ChangeStatus(VehicleStatus.Sold);

        await _context.SaveChangesAsync(ct);

        await _accountingService.PostAutomaticInvoiceJournalAsync(invoice, ct);
    }
}
