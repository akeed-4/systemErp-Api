using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantWhatsAppIntegration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    BusinessId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WabaId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PhoneNumberId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayPhoneNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    VerifiedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    QualityRating = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    MessagingLimit = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IsCoexistence = table.Column<bool>(type: "bit", nullable: false),
                    AccessTokenEncrypted = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RegistrationPinEncrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InvoiceTemplateName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    InvoiceTemplateLanguage = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    InvoiceTemplateStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ConnectedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConnectedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DisconnectedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantWhatsAppIntegration", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppMessage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IntegrationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DocumentNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecipientPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TemplateName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    TemplateLanguage = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    MediaId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResendOfId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReadAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PricingCategory = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Billable = table.Column<bool>(type: "bit", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppMessage", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "PlatformModuleDefinition",
                columns: new[] { "Key", "DescriptionAr", "DescriptionEn", "Icon", "IsActive", "NameAr", "NameEn", "Number", "SortOrder" },
                values: new object[] { "whatsapp", "ربط رقم واتساب المنشأة وإرسال الفواتير للعملاء منه.", "Connect the company WhatsApp number and send invoices to customers from it.", "chat", true, "واتساب للأعمال", "WhatsApp Business", 5, 5 });

            migrationBuilder.CreateIndex(
                name: "IX_TenantWhatsAppIntegration_PhoneNumberId",
                table: "TenantWhatsAppIntegration",
                column: "PhoneNumberId",
                unique: true,
                filter: "[Status] <> N'Disconnected'");

            migrationBuilder.CreateIndex(
                name: "IX_TenantWhatsAppIntegration_TenantId",
                table: "TenantWhatsAppIntegration",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessage_TenantId",
                table: "WhatsAppMessage",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessage_TenantId_DocumentType_DocumentId",
                table: "WhatsAppMessage",
                columns: new[] { "TenantId", "DocumentType", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessage_TenantId_DocumentType_DocumentId_RecipientPhone",
                table: "WhatsAppMessage",
                columns: new[] { "TenantId", "DocumentType", "DocumentId", "RecipientPhone" },
                unique: true,
                filter: "[Status] = N'Sending' AND [DocumentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessage_TenantId_IdempotencyKey",
                table: "WhatsAppMessage",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessage_TenantId_ProviderMessageId",
                table: "WhatsAppMessage",
                columns: new[] { "TenantId", "ProviderMessageId" },
                unique: true,
                filter: "[ProviderMessageId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantWhatsAppIntegration");

            migrationBuilder.DropTable(
                name: "WhatsAppMessage");

            migrationBuilder.DeleteData(
                table: "PlatformModuleDefinition",
                keyColumn: "Key",
                keyValue: "whatsapp");
        }
    }
}
