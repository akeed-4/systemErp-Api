using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class LibraryMaterialRequirements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "ProductId",
                table: "SiteMaterialUsage",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "ResourceId",
                table: "SiteMaterialUsage",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ProductId",
                table: "ProjectMaterialRequirement",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.CreateIndex(
                name: "IX_SiteMaterialUsage_ResourceId",
                table: "SiteMaterialUsage",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMaterialRequirement_ResourceId",
                table: "ProjectMaterialRequirement",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMaterialRequirement_TenantId_ContractId_ResourceId",
                table: "ProjectMaterialRequirement",
                columns: new[] { "TenantId", "ContractId", "ResourceId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectMaterialRequirement_ConstructionResource_ResourceId",
                table: "ProjectMaterialRequirement",
                column: "ResourceId",
                principalTable: "ConstructionResource",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SiteMaterialUsage_ConstructionResource_ResourceId",
                table: "SiteMaterialUsage",
                column: "ResourceId",
                principalTable: "ConstructionResource",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectMaterialRequirement_ConstructionResource_ResourceId",
                table: "ProjectMaterialRequirement");

            migrationBuilder.DropForeignKey(
                name: "FK_SiteMaterialUsage_ConstructionResource_ResourceId",
                table: "SiteMaterialUsage");

            migrationBuilder.DropIndex(
                name: "IX_SiteMaterialUsage_ResourceId",
                table: "SiteMaterialUsage");

            migrationBuilder.DropIndex(
                name: "IX_ProjectMaterialRequirement_ResourceId",
                table: "ProjectMaterialRequirement");

            migrationBuilder.DropIndex(
                name: "IX_ProjectMaterialRequirement_TenantId_ContractId_ResourceId",
                table: "ProjectMaterialRequirement");

            migrationBuilder.DropColumn(
                name: "ResourceId",
                table: "SiteMaterialUsage");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProductId",
                table: "SiteMaterialUsage",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ProductId",
                table: "ProjectMaterialRequirement",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
