using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class LoyaltyPendingPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LoyaltyPointsRevoked",
                table: "PosTransaction",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PointsPendingDays",
                table: "PosPromotionSettings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PendingPoints",
                table: "CustomerLoyalty",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "CustomerLoyalty",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateTable(
                name: "LoyaltyPointEntry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Points = table.Column<int>(type: "int", nullable: false),
                    OriginalPoints = table.Column<int>(type: "int", nullable: false),
                    AvailableAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoyaltyPointEntry", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyPointEntry_TenantId",
                table: "LoyaltyPointEntry",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyPointEntry_TenantId_Status_AvailableAt",
                table: "LoyaltyPointEntry",
                columns: new[] { "TenantId", "Status", "AvailableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyPointEntry_TenantId_TransactionId",
                table: "LoyaltyPointEntry",
                columns: new[] { "TenantId", "TransactionId" },
                unique: true);

            // ما سحبته المرتجعات السابقة من نقاط كل فاتورة: حتى لا يُسحب مرة أخرى
            migrationBuilder.Sql(@"UPDATE t SET LoyaltyPointsRevoked = r.Revoked
FROM PosTransaction t
JOIN (SELECT OriginalTransactionId, SUM(PointsRevoked) AS Revoked FROM PosSalesReturn GROUP BY OriginalTransactionId) r ON r.OriginalTransactionId = t.Id;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LoyaltyPointEntry");

            migrationBuilder.DropColumn(
                name: "LoyaltyPointsRevoked",
                table: "PosTransaction");

            migrationBuilder.DropColumn(
                name: "PointsPendingDays",
                table: "PosPromotionSettings");

            migrationBuilder.DropColumn(
                name: "PendingPoints",
                table: "CustomerLoyalty");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "CustomerLoyalty");
        }
    }
}
