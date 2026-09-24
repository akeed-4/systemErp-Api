namespace ERP.Core.DTOs.CarShowroom;

public partial class CreateCarSalesContractDto
{
    public string ContractNumber { get; set; } = string.Empty;
    public CarSalesCycleType CycleType { get; set; }
    public DateTime Date { get; set; }
    public Guid? CustomerId { get; set; }
    public BuyerType? BuyerType { get; set; }
    public string BuyerName { get; set; } = string.Empty;
    public string BuyerNationalIdOrCr { get; set; } = string.Empty;
    public DateTime? IdExpiryDate { get; set; }
    public string BuyerPhone { get; set; } = string.Empty;
    public string? BuyerAltPhone { get; set; }
    public string? BuyerEmail { get; set; }
    public string? BuyerAddress { get; set; }
    public string? BuyerCity { get; set; }
    public string? BuyerDistrict { get; set; }
    public string? AuthorizedSignatoryName { get; set; }
    public string? AuthorizedSignatoryId { get; set; }
    public Guid? FinancingBankId { get; set; }
    public string? FinancingBankName { get; set; }
    public string? BankApprovalNumber { get; set; }
    public DateTime? BankApprovalDate { get; set; }
    public string? BankDisbursementStatus { get; set; }
    public string? CorporatePoNumber { get; set; }
    public string? TenderNumber { get; set; }
    public decimal? DownPaymentAmount { get; set; }
    public string? DownPaymentReceiptNo { get; set; }
    public DateTime? DownPaymentDate { get; set; }
    public decimal? FinancedAmount { get; set; }
    public decimal? MonthlyInstallment { get; set; }
    public int? FinanceTenorMonths { get; set; }
    public Guid VehicleId { get; set; }
    public string Vin { get; set; } = string.Empty;
    public string? EngineNumber { get; set; }
    public string? CustomsCardNumber { get; set; }
    public string VehicleDescription { get; set; } = string.Empty;
    public string? BrandNameAr { get; set; }
    public string? ModelNameAr { get; set; }
    public int? Year { get; set; }
    public string? ColorExterior { get; set; }
    public string? ColorInterior { get; set; }
    public VehicleCondition Condition { get; set; } = VehicleCondition.New;
    public decimal? Mileage { get; set; }
    public string? FuelType { get; set; }
    public string? Transmission { get; set; }
    public string? Location { get; set; }
    public decimal CostPrice { get; set; }
    public decimal? AdditionalCosts { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal? DiscountAmount { get; set; }
    public decimal? NetPriceBeforeVat { get; set; }
    public decimal ProfitMargin { get; set; }
    public VatMode VatMode { get; set; } = VatMode.Standard_15;
    public decimal VatAmount { get; set; }
    public decimal TotalWithVat { get; set; }
    public decimal? ProfitMarginVat { get; set; }
    public string PaymentMethod { get; set; } = "cash";
    public string? PlateRegistrationType { get; set; }
    public string? PlateLettersAr { get; set; }
    public string? PlateDigitsAr { get; set; }
    public string? PlateLettersEn { get; set; }
    public decimal? RegistrationFee { get; set; }
    public string? InsuranceCompany { get; set; }
    public string? InsuranceType { get; set; }
    public string? InsurancePolicyNumber { get; set; }
    public decimal? InsuranceCost { get; set; }
    public string? TrafficTammStatus { get; set; }
    public int? WarrantyYears { get; set; }
    public int? WarrantyKm { get; set; }
    public string? FreeServicePackage { get; set; }
    public string? InstalledAccessories { get; set; }
    public decimal? AccessoriesCost { get; set; }
    public SalesContractStatus Status { get; set; } = SalesContractStatus.Draft;
    public string? AllocatedVin { get; set; }
    public DateTime? AllocatedAt { get; set; }
    public string? PdiInspectionNotes { get; set; }
    public string? HandoverProtocolNumber { get; set; }
    public string? HandoverSignee { get; set; }
    public string? HandoverSigneeNationalId { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? DeliveryDate { get; set; }
    public string? DeliveryLocation { get; set; }
    public string? SalespersonName { get; set; }
    public string? SalesBranch { get; set; }
    public string? Notes { get; set; }
    public Guid? InvoiceId { get; set; }
}
