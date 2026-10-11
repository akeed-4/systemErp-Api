using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddZatcaUserUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ZatcaChain");

            migrationBuilder.CreateTable(
                name: "ZatcaUnit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Environment = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DeviceSerial = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ComplianceRequestId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Csid = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BinarySecurityToken = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CertificatePem = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SecretKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PrivateKeyPem = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    OnboardedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Counter = table.Column<long>(type: "bigint", nullable: false),
                    LastHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZatcaUnit", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ZatcaUnit_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ZatcaUnit_TenantId",
                table: "ZatcaUnit",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ZatcaUnit_UserId",
                table: "ZatcaUnit",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ZatcaUnit");

            migrationBuilder.CreateTable(
                name: "ZatcaChain",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Counter = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZatcaChain", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ZatcaChain_TenantId",
                table: "ZatcaChain",
                column: "TenantId");
        }
    }
}
