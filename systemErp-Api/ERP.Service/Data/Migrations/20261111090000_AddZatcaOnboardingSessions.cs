using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ERP.Service.Data;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261111090000_AddZatcaOnboardingSessions")]
    public partial class AddZatcaOnboardingSessions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ZatcaOnboardingSession",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Environment = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    DeviceSerial = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InvoiceTypes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Csr = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ComplianceRequestId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ComplianceBinarySecurityToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ComplianceSecret = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CertificatePem = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PrivateKeyPem = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProductionRequestId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_ZatcaOnboardingSession", x => x.Id));

            migrationBuilder.CreateTable(
                name: "ZatcaComplianceTest",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsSimplified = table.Column<bool>(type: "bit", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Uuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SignedXml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    HttpStatus = table.Column<int>(type: "int", nullable: true),
                    ResponseMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZatcaComplianceTest", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ZatcaComplianceTest_ZatcaOnboardingSession_SessionId",
                        column: x => x.SessionId,
                        principalTable: "ZatcaOnboardingSession",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ZatcaComplianceTest_TenantId_SessionId_DocumentKey",
                table: "ZatcaComplianceTest",
                columns: new[] { "TenantId", "SessionId", "DocumentKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ZatcaComplianceTest_TenantId_SessionId_Status",
                table: "ZatcaComplianceTest",
                columns: new[] { "TenantId", "SessionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ZatcaComplianceTest_TenantId",
                table: "ZatcaComplianceTest",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ZatcaComplianceTest_SessionId",
                table: "ZatcaComplianceTest",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ZatcaOnboardingSession_TenantId_UserId_Environment_StartedAt",
                table: "ZatcaOnboardingSession",
                columns: new[] { "TenantId", "UserId", "Environment", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ZatcaOnboardingSession_TenantId",
                table: "ZatcaOnboardingSession",
                column: "TenantId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ZatcaComplianceTest");
            migrationBuilder.DropTable(name: "ZatcaOnboardingSession");
        }
    }
}
