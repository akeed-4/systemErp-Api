using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ContractTenders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TenderDeadline",
                table: "CommercialContract",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TenderDecidedAt",
                table: "CommercialContract",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TenderIssueDate",
                table: "CommercialContract",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenderNumber",
                table: "CommercialContract",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenderRequirements",
                table: "CommercialContract",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenderResultNote",
                table: "CommercialContract",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenderStatus",
                table: "CommercialContract",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContractTenderRevision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionNo = table.Column<int>(type: "int", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TenderValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ContractValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    EstimatedCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LinesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractTenderRevision", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractTenderRevision_CommercialContract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "CommercialContract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialContract_TenantId_TenderStatus",
                table: "CommercialContract",
                columns: new[] { "TenantId", "TenderStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractTenderRevision_ContractId",
                table: "ContractTenderRevision",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractTenderRevision_TenantId",
                table: "ContractTenderRevision",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractTenderRevision_TenantId_ContractId_RevisionNo",
                table: "ContractTenderRevision",
                columns: new[] { "TenantId", "ContractId", "RevisionNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractTenderRevision");

            migrationBuilder.DropIndex(
                name: "IX_CommercialContract_TenantId_TenderStatus",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "TenderDeadline",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "TenderDecidedAt",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "TenderIssueDate",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "TenderNumber",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "TenderRequirements",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "TenderResultNote",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "TenderStatus",
                table: "CommercialContract");
        }
    }
}
