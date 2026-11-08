using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class SubcontractProjectLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MainContractItemId",
                table: "CommercialContractItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentContractId",
                table: "CommercialContract",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommercialContractItem_TenantId_MainContractItemId",
                table: "CommercialContractItem",
                columns: new[] { "TenantId", "MainContractItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialContract_ParentContractId",
                table: "CommercialContract",
                column: "ParentContractId");

            migrationBuilder.AddForeignKey(
                name: "FK_CommercialContract_CommercialContract_ParentContractId",
                table: "CommercialContract",
                column: "ParentContractId",
                principalTable: "CommercialContract",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CommercialContract_CommercialContract_ParentContractId",
                table: "CommercialContract");

            migrationBuilder.DropIndex(
                name: "IX_CommercialContractItem_TenantId_MainContractItemId",
                table: "CommercialContractItem");

            migrationBuilder.DropIndex(
                name: "IX_CommercialContract_ParentContractId",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "MainContractItemId",
                table: "CommercialContractItem");

            migrationBuilder.DropColumn(
                name: "ParentContractId",
                table: "CommercialContract");
        }
    }
}
