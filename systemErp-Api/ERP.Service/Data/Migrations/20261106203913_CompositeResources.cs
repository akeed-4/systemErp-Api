using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class CompositeResources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConstructionResourceComponent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComponentResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    WastePercent = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConstructionResourceComponent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConstructionResourceComponent_ConstructionResource_ComponentResourceId",
                        column: x => x.ComponentResourceId,
                        principalTable: "ConstructionResource",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConstructionResourceComponent_ConstructionResource_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "ConstructionResource",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConstructionResourceComponent_ComponentResourceId",
                table: "ConstructionResourceComponent",
                column: "ComponentResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ConstructionResourceComponent_ResourceId_ComponentResourceId",
                table: "ConstructionResourceComponent",
                columns: new[] { "ResourceId", "ComponentResourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConstructionResourceComponent_TenantId",
                table: "ConstructionResourceComponent",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConstructionResourceComponent");
        }
    }
}
