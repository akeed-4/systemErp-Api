using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ContractItemWorkItemSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WorkItemId",
                table: "CommercialContractItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommercialContractItem_TenantId_WorkItemId",
                table: "CommercialContractItem",
                columns: new[] { "TenantId", "WorkItemId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CommercialContractItem_TenantId_WorkItemId",
                table: "CommercialContractItem");

            migrationBuilder.DropColumn(
                name: "WorkItemId",
                table: "CommercialContractItem");
        }
    }
}
