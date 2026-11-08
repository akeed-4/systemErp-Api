using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ContractVariations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "VariationId",
                table: "CommercialContractItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContractVariation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SequenceNo = table.Column<int>(type: "int", nullable: false),
                    VariationNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    VariationDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ClientReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ExtensionDays = table.Column<int>(type: "int", nullable: false),
                    ValueChange = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CostChange = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ReturnNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecidedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ContractValueBefore = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    ContractValueAfter = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    EndDateBefore = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndDateAfter = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractVariation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractVariation_CommercialContract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "CommercialContract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContractVariationLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VariationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LineCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    SectionName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LineKind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    QuantityChange = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    EstimatedUnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CostAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    QuantityBefore = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    QuantityAfter = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractVariationLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractVariationLine_CommercialContractItem_ContractItemId",
                        column: x => x.ContractItemId,
                        principalTable: "CommercialContractItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContractVariationLine_ContractVariation_VariationId",
                        column: x => x.VariationId,
                        principalTable: "ContractVariation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialContractItem_TenantId_VariationId",
                table: "CommercialContractItem",
                columns: new[] { "TenantId", "VariationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractVariation_ContractId",
                table: "ContractVariation",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractVariation_TenantId",
                table: "ContractVariation",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractVariation_TenantId_ContractId_SequenceNo",
                table: "ContractVariation",
                columns: new[] { "TenantId", "ContractId", "SequenceNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractVariation_TenantId_VariationNumber",
                table: "ContractVariation",
                columns: new[] { "TenantId", "VariationNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractVariationLine_ContractItemId",
                table: "ContractVariationLine",
                column: "ContractItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractVariationLine_TenantId",
                table: "ContractVariationLine",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractVariationLine_VariationId",
                table: "ContractVariationLine",
                column: "VariationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractVariationLine");

            migrationBuilder.DropTable(
                name: "ContractVariation");

            migrationBuilder.DropIndex(
                name: "IX_CommercialContractItem_TenantId_VariationId",
                table: "CommercialContractItem");

            migrationBuilder.DropColumn(
                name: "VariationId",
                table: "CommercialContractItem");
        }
    }
}
