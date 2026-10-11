using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExtendAgreementsDeliveryContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ContractId",
                table: "DeliveryReturnNote",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContractNumber",
                table: "DeliveryReturnNote",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GrandTotal",
                table: "DeliveryReturnNote",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "DeliveryReturnNote",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartyName",
                table: "DeliveryReturnNote",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ReturnReason",
                table: "DeliveryReturnNote",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Subtotal",
                table: "DeliveryReturnNote",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VatTotal",
                table: "DeliveryReturnNote",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalAfterVat",
                table: "DeliveryReturnItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "DeliveryReturnItem",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "DeliveryReturnItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VatAmount",
                table: "DeliveryReturnItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VatRate",
                table: "DeliveryReturnItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 15m);

            migrationBuilder.AddColumn<string>(
                name: "Sku",
                table: "DeliveryNoteItem",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalAfterVat",
                table: "DeliveryNoteItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalBeforeVat",
                table: "DeliveryNoteItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "DeliveryNoteItem",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "DeliveryNoteItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VatAmount",
                table: "DeliveryNoteItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VatRate",
                table: "DeliveryNoteItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 15m);

            migrationBuilder.AddColumn<string>(
                name: "DriverName",
                table: "DeliveryNote",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GrandTotal",
                table: "DeliveryNote",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "DeliveryNote",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartyPhone",
                table: "DeliveryNote",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartyTaxNumber",
                table: "DeliveryNote",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Subtotal",
                table: "DeliveryNote",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VatTotal",
                table: "DeliveryNote",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "VehiclePlate",
                table: "DeliveryNote",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WarehouseLocation",
                table: "DeliveryNote",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliverableDescription",
                table: "ContractMilestone",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NetPayableAmount",
                table: "ContractMilestone",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RetentionDeductionAmount",
                table: "ContractMilestone",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RetentionDeductionPercent",
                table: "ContractMilestone",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "AgreementId",
                table: "CommercialOrder",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgreementNumber",
                table: "CommercialOrder",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutoRenew",
                table: "CommercialContract",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "CostCenterId",
                table: "CommercialContract",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "CommercialContract",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "SAR");

            migrationBuilder.AddColumn<int>(
                name: "DurationMonths",
                table: "CommercialContract",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LatePenaltyPerDay",
                table: "CommercialContract",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxPenaltyPercent",
                table: "CommercialContract",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PartyCrNumber",
                table: "CommercialContract",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceNumber",
                table: "CommercialContract",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepresentativeName",
                table: "CommercialContract",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RetentionAmount",
                table: "CommercialContract",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RetentionPercent",
                table: "CommercialContract",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "RetentionReleaseDate",
                table: "CommercialContract",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScopeOfWork",
                table: "CommercialContract",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignedByCompany",
                table: "CommercialContract",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignedByParty",
                table: "CommercialContract",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SignedDate",
                table: "CommercialContract",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsAndConditions",
                table: "CommercialContract",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TitleEn",
                table: "CommercialContract",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Barcode",
                table: "AgreementItem",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Discount",
                table: "AgreementItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ItemCode",
                table: "AgreementItem",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxQuantity",
                table: "AgreementItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MinQuantity",
                table: "AgreementItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "AgreementItem",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "AgreementItem",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "active");

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "AgreementItem",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UsedQuantity",
                table: "AgreementItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VatRate",
                table: "AgreementItem",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 15m);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Agreement",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "SAR");

            migrationBuilder.AddColumn<string>(
                name: "PartyNameEn",
                table: "Agreement",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceNo",
                table: "Agreement",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommercialContractItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalWithVat = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialContractItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommercialContractItem_CommercialContract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "CommercialContract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialContractItem_ContractId",
                table: "CommercialContractItem",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialContractItem_TenantId",
                table: "CommercialContractItem",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommercialContractItem");

            migrationBuilder.DropColumn(
                name: "ContractId",
                table: "DeliveryReturnNote");

            migrationBuilder.DropColumn(
                name: "ContractNumber",
                table: "DeliveryReturnNote");

            migrationBuilder.DropColumn(
                name: "GrandTotal",
                table: "DeliveryReturnNote");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "DeliveryReturnNote");

            migrationBuilder.DropColumn(
                name: "PartyName",
                table: "DeliveryReturnNote");

            migrationBuilder.DropColumn(
                name: "ReturnReason",
                table: "DeliveryReturnNote");

            migrationBuilder.DropColumn(
                name: "Subtotal",
                table: "DeliveryReturnNote");

            migrationBuilder.DropColumn(
                name: "VatTotal",
                table: "DeliveryReturnNote");

            migrationBuilder.DropColumn(
                name: "TotalAfterVat",
                table: "DeliveryReturnItem");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "DeliveryReturnItem");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "DeliveryReturnItem");

            migrationBuilder.DropColumn(
                name: "VatAmount",
                table: "DeliveryReturnItem");

            migrationBuilder.DropColumn(
                name: "VatRate",
                table: "DeliveryReturnItem");

            migrationBuilder.DropColumn(
                name: "Sku",
                table: "DeliveryNoteItem");

            migrationBuilder.DropColumn(
                name: "TotalAfterVat",
                table: "DeliveryNoteItem");

            migrationBuilder.DropColumn(
                name: "TotalBeforeVat",
                table: "DeliveryNoteItem");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "DeliveryNoteItem");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "DeliveryNoteItem");

            migrationBuilder.DropColumn(
                name: "VatAmount",
                table: "DeliveryNoteItem");

            migrationBuilder.DropColumn(
                name: "VatRate",
                table: "DeliveryNoteItem");

            migrationBuilder.DropColumn(
                name: "DriverName",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "GrandTotal",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "PartyPhone",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "PartyTaxNumber",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "Subtotal",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "VatTotal",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "VehiclePlate",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "WarehouseLocation",
                table: "DeliveryNote");

            migrationBuilder.DropColumn(
                name: "DeliverableDescription",
                table: "ContractMilestone");

            migrationBuilder.DropColumn(
                name: "NetPayableAmount",
                table: "ContractMilestone");

            migrationBuilder.DropColumn(
                name: "RetentionDeductionAmount",
                table: "ContractMilestone");

            migrationBuilder.DropColumn(
                name: "RetentionDeductionPercent",
                table: "ContractMilestone");

            migrationBuilder.DropColumn(
                name: "AgreementId",
                table: "CommercialOrder");

            migrationBuilder.DropColumn(
                name: "AgreementNumber",
                table: "CommercialOrder");

            migrationBuilder.DropColumn(
                name: "AutoRenew",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "CostCenterId",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "DurationMonths",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "LatePenaltyPerDay",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "MaxPenaltyPercent",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "PartyCrNumber",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "ReferenceNumber",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "RepresentativeName",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "RetentionAmount",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "RetentionPercent",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "RetentionReleaseDate",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "ScopeOfWork",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "SignedByCompany",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "SignedByParty",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "SignedDate",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "TermsAndConditions",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "TitleEn",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "Barcode",
                table: "AgreementItem");

            migrationBuilder.DropColumn(
                name: "Discount",
                table: "AgreementItem");

            migrationBuilder.DropColumn(
                name: "ItemCode",
                table: "AgreementItem");

            migrationBuilder.DropColumn(
                name: "MaxQuantity",
                table: "AgreementItem");

            migrationBuilder.DropColumn(
                name: "MinQuantity",
                table: "AgreementItem");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "AgreementItem");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "AgreementItem");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "AgreementItem");

            migrationBuilder.DropColumn(
                name: "UsedQuantity",
                table: "AgreementItem");

            migrationBuilder.DropColumn(
                name: "VatRate",
                table: "AgreementItem");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Agreement");

            migrationBuilder.DropColumn(
                name: "PartyNameEn",
                table: "Agreement");

            migrationBuilder.DropColumn(
                name: "ReferenceNo",
                table: "Agreement");
        }
    }
}
