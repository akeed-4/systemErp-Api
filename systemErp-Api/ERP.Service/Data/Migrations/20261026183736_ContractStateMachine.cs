using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ContractStateMachine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "active_execution");

            migrationBuilder.DeleteData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "approved_signed");

            migrationBuilder.DeleteData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "final_closed");

            migrationBuilder.DeleteData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "initial_inspection");

            migrationBuilder.DeleteData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "legal_review");

            migrationBuilder.DeleteData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "milestone_billing");

            migrationBuilder.DeleteData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "normal_agreement");

            migrationBuilder.DeleteData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "normal_delivery");

            migrationBuilder.DeleteData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "normal_delivery_return");

            migrationBuilder.DeleteData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "normal_milestone_invoice");

            migrationBuilder.AddColumn<string>(
                name: "Phase",
                table: "ContractStageDefinition",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShortNameAr",
                table: "ContractStageDefinition",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShortNameEn",
                table: "ContractStageDefinition",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "SlaDays",
                table: "ContractStageDefinition",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AdvancePercent",
                table: "CommercialContract",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "GuaranteeAmount",
                table: "CommercialContract",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuaranteeBank",
                table: "CommercialContract",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GuaranteeExpiry",
                table: "CommercialContract",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuaranteeNumber",
                table: "CommercialContract",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSimplified",
                table: "CommercialContract",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ReviewRound",
                table: "CommercialContract",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "StageChangedAt",
                table: "CommercialContract",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContractApproval",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Round = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractApproval", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractApproval_CommercialContract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "CommercialContract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContractAttachment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    UploadedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractAttachment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractAttachment_CommercialContract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "CommercialContract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContractInvoice",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DeliveryNoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReturnNoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RelatedInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    AdvanceDeduction = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RetentionAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractInvoice", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractInvoice_CommercialContract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "CommercialContract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContractStatusLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TransitionId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractStatusLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractStatusLog_CommercialContract_ContractId",
                        column: x => x.ContractId,
                        principalTable: "CommercialContract",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContractTransition",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ToStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequiredActions = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Condition = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RequiresNote = table.Column<bool>(type: "bit", nullable: false),
                    Tone = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractTransition", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContractTypeDefinition",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Workflow = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Billing = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SimpleLimit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    RequiresGuarantee = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractTypeDefinition", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "draft",
                columns: new[] { "DescAr", "DescEn", "Phase", "ShortNameAr", "ShortNameEn", "SlaDays", "WorkflowType" },
                values: new object[] { "صياغة البنود والكميات والأسعار والشروط", "Drafting items, quantities, prices and terms", "pre_signing", "مسودة", "Draft", 7, "contract" });

            migrationBuilder.InsertData(
                table: "ContractStageDefinition",
                columns: new[] { "Id", "AdvanceActions", "AllowedActions", "BadgeColor", "DescAr", "DescEn", "HeaderColor", "Icon", "IsActive", "NameAr", "NameEn", "Number", "Phase", "ShortNameAr", "ShortNameEn", "SlaDays", "WorkflowType" },
                values: new object[,]
                {
                    { "active", "close", "edit,post,close", "bg-emerald-950 text-emerald-300 border-emerald-800", "الاستلامات والمرتجعات والفواتير المرحلية", "Receipts, returns and progress invoices", "from-emerald-700 to-emerald-800", "engineering", true, "ساري وقيد التنفيذ", "Active Execution", 4, "execution", "ساري", "Active", null, "contract" },
                    { "approval", "approve,sign", "approve,sign,reject", "bg-blue-950 text-blue-300 border-blue-800", "اعتماد صاحب الصلاحية بعد النسخة الموقعة والضمان البنكي", "Authorized approval after the signed copy and bank guarantee", "from-blue-700 to-blue-800", "draw", true, "الاعتماد والتوقيع", "Approval and Signing", 3, "pre_signing", "اعتماد", "Approval", 3, "contract" },
                    { "cancelled", "", "", "bg-rose-950 text-rose-300 border-rose-800", "أُلغي قبل أن يصبح سارياً", "Cancelled before becoming active", "from-rose-700 to-rose-800", "cancel", true, "ملغي قبل التفعيل", "Cancelled", 8, "side", "ملغي", "Cancelled", null, "contract" },
                    { "closed", "", "post", "bg-teal-950 text-teal-300 border-teal-800", "الإفراج عن المحتجز والتسوية الختامية", "Retention release and final settlement", "from-teal-700 to-teal-800", "verified", true, "الإغلاق النهائي", "Final Closure", 6, "closing", "مغلق", "Closed", null, "contract" },
                    { "initial_acceptance", "close", "edit,post,reject,close", "bg-cyan-950 text-cyan-300 border-cyan-800", "فحص المخرجات واعتماد محاضر الاستلام والفوترة النهائية", "Inspecting deliverables, acceptance records and final billing", "from-cyan-700 to-cyan-800", "checklist", true, "الاستلام الأولي", "Initial Acceptance", 5, "closing", "استلام", "Acceptance", 14, "contract" },
                    { "review", "review", "review,reject", "bg-amber-950 text-amber-300 border-amber-800", "موافقة المراجع القانوني والمراجع المالي معاً", "Approval by both the legal and the financial reviewer", "from-amber-700 to-amber-800", "gavel", true, "المراجعة القانونية والمالية", "Legal and Financial Review", 2, "pre_signing", "مراجعة", "Review", 5, "contract" },
                    { "suspended", "", "", "bg-orange-950 text-orange-300 border-orange-800", "تنفيذ العقد موقوف بقرار مسبب", "Execution suspended by a reasoned decision", "from-orange-700 to-orange-800", "pause_circle", true, "موقوف مؤقتاً", "Suspended", 7, "side", "موقوف", "Suspended", null, "contract" }
                });

            migrationBuilder.InsertData(
                table: "ContractTransition",
                columns: new[] { "Id", "Condition", "FromStatus", "IsActive", "NameAr", "NameEn", "RequiredActions", "RequiresNote", "SortOrder", "ToStatus", "Tone" },
                values: new object[,]
                {
                    { 1, "complete_data", "draft", true, "إرسال للمراجعة", "Submit for review", "submit", false, 1, "review", "primary" },
                    { 2, "both_reviews", "review", true, "موافقة المراجعة", "Approve review", "legal_review,financial_review", false, 1, "approval", "primary" },
                    { 3, null, "review", true, "إعادة للمسودة", "Return to draft", "legal_review,financial_review", true, 2, "draft", "neutral" },
                    { 4, "signed_guarantee_limit", "approval", true, "اعتماد وتفعيل", "Approve & activate", "approve", false, 1, "active", "primary" },
                    { 5, null, "approval", true, "رفض الاعتماد", "Reject approval", "approve", true, 2, "draft", "neutral" },
                    { 6, "all_received", "active", true, "استلام أولي", "Initial acceptance", "inspect", false, 1, "initial_acceptance", "primary" },
                    { 7, null, "active", true, "إيقاف", "Suspend", "approve", true, 2, "suspended", "danger" },
                    { 8, null, "suspended", true, "استئناف", "Resume", "approve", true, 1, "active", "primary" },
                    { 9, "all_billed_retention_released", "initial_acceptance", true, "إغلاق نهائي", "Close", "approve", false, 1, "closed", "primary" },
                    { 10, null, "draft", true, "إلغاء", "Cancel", "approve", true, 9, "cancelled", "danger" },
                    { 11, null, "review", true, "إلغاء", "Cancel", "approve", true, 9, "cancelled", "danger" },
                    { 12, null, "approval", true, "إلغاء", "Cancel", "approve", true, 9, "cancelled", "danger" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContractApproval_ContractId",
                table: "ContractApproval",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractApproval_TenantId",
                table: "ContractApproval",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractApproval_TenantId_ContractId_Round",
                table: "ContractApproval",
                columns: new[] { "TenantId", "ContractId", "Round" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractAttachment_ContractId",
                table: "ContractAttachment",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractAttachment_TenantId",
                table: "ContractAttachment",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractAttachment_TenantId_ContractId",
                table: "ContractAttachment",
                columns: new[] { "TenantId", "ContractId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractInvoice_ContractId",
                table: "ContractInvoice",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractInvoice_TenantId",
                table: "ContractInvoice",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractInvoice_TenantId_ContractId",
                table: "ContractInvoice",
                columns: new[] { "TenantId", "ContractId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractInvoice_TenantId_DeliveryNoteId_Kind",
                table: "ContractInvoice",
                columns: new[] { "TenantId", "DeliveryNoteId", "Kind" },
                unique: true,
                filter: "[DeliveryNoteId] IS NOT NULL AND [Kind] <> N'credit_note'");

            migrationBuilder.CreateIndex(
                name: "IX_ContractStatusLog_ContractId",
                table: "ContractStatusLog",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractStatusLog_TenantId",
                table: "ContractStatusLog",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractStatusLog_TenantId_ContractId",
                table: "ContractStatusLog",
                columns: new[] { "TenantId", "ContractId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractTransition_FromStatus_ToStatus",
                table: "ContractTransition",
                columns: new[] { "FromStatus", "ToStatus" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractTypeDefinition_TenantId",
                table: "ContractTypeDefinition",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractTypeDefinition_TenantId_Code",
                table: "ContractTypeDefinition",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            // ---------- ترحيل البيانات القائمة ----------

            // 1) المراحل القديمة ← الحالات الجديدة. «المستخلصات والفوترة» ومراحل دورة الشراء العادية كلها «ساري»:
            //    الاستلام والمرتجع والفوترة مستندات داخل الحالة لا مراحل.
            migrationBuilder.Sql(@"
UPDATE [CommercialContract] SET [Stage] = CASE [Stage]
    WHEN 'legal_review' THEN 'review'
    WHEN 'approved_signed' THEN 'approval'
    WHEN 'active_execution' THEN 'active'
    WHEN 'milestone_billing' THEN 'active'
    WHEN 'initial_inspection' THEN 'initial_acceptance'
    WHEN 'final_closed' THEN 'closed'
    WHEN 'normal_agreement' THEN 'active'
    WHEN 'normal_delivery' THEN 'active'
    WHEN 'normal_delivery_return' THEN 'active'
    WHEN 'normal_milestone_invoice' THEN 'active'
    ELSE [Stage] END;

-- الموقوف والمنتهي في الحالة القديمة: موقوف، أو ملغي قبل التفعيل / مغلق بعده
UPDATE [CommercialContract] SET [Stage] = 'suspended' WHERE [Status] = 'Suspended' AND [Stage] <> 'closed';
UPDATE [CommercialContract] SET [Stage] = CASE WHEN [Stage] IN ('draft', 'review', 'approval') THEN 'cancelled' ELSE 'closed' END
WHERE [Status] = 'Terminated';
UPDATE [CommercialContract] SET [Stage] = 'draft'
WHERE [Stage] NOT IN ('draft', 'review', 'approval', 'active', 'initial_acceptance', 'closed', 'suspended', 'cancelled');

UPDATE [CommercialContract] SET
    [Status] = CASE [Stage]
        WHEN 'draft' THEN 'Draft' WHEN 'review' THEN 'UnderReview' WHEN 'approval' THEN 'UnderReview'
        WHEN 'active' THEN 'Active' WHEN 'initial_acceptance' THEN 'Active' WHEN 'closed' THEN 'Completed'
        WHEN 'suspended' THEN 'Suspended' ELSE 'Terminated' END,
    [StageChangedAt] = COALESCE([UpdatedAt], [CreatedAt]),
    [ReviewRound] = 1;");

            // 2) أنواع العقود القديمة ← الأنواع الأربعة الافتراضية (ما لا يطابق يُسجَّل «عقد قائم» بمسار مبسط وفوترة مباشرة كما كان يعمل)
            migrationBuilder.Sql(@"
UPDATE [CommercialContract] SET [ContractType] = CASE
    WHEN [ContractType] IN ('supply_materials', 'monthly_maintenance', 'construction_project', 'existing_contract') THEN [ContractType]
    WHEN [ContractType] IN ('supply', 'normal_purchasing', 'credit_sale', 'credit_sales') THEN 'supply_materials'
    WHEN [ContractType] IN ('sla_service', 'maintenance', 'service') THEN 'monthly_maintenance'
    WHEN [ContractType] = 'construction' THEN 'construction_project'
    ELSE 'existing_contract' END;");

            // 3) الأنواع الافتراضية لكل منشأة قائمة (المنشآت الجديدة تُزرع عند التسجيل)
            migrationBuilder.Sql(@"
INSERT INTO [ContractTypeDefinition] ([Id], [TenantId], [CreatedAt], [Code], [NameAr], [NameEn], [Workflow], [Billing], [SimpleLimit], [RequiresGuarantee], [IsActive], [SortOrder])
SELECT NEWID(), t.[TenantId], SYSUTCDATETIME(), v.[Code], v.[NameAr], v.[NameEn], v.[Workflow], v.[Billing], NULL, v.[RequiresGuarantee], 1, v.[SortOrder]
FROM (SELECT [Id] AS [TenantId] FROM [Tenant] UNION SELECT DISTINCT [TenantId] FROM [CommercialContract]) t
CROSS JOIN (VALUES
    ('supply_materials', N'توريد مواد', N'Materials Supply', 'full', 'after_receipt', 1, 1),
    ('monthly_maintenance', N'صيانة شهرية', N'Monthly Maintenance', 'simple', 'direct', 0, 2),
    ('construction_project', N'مشروع إنشائي', N'Construction Project', 'full', 'after_receipt', 1, 3),
    ('existing_contract', N'تسجيل عقد قائم', N'Register Existing Contract', 'simple', 'direct', 0, 4)
) v([Code], [NameAr], [NameEn], [Workflow], [Billing], [RequiresGuarantee], [SortOrder])
WHERE NOT EXISTS (SELECT 1 FROM [ContractTypeDefinition] x WHERE x.[TenantId] = t.[TenantId] AND x.[Code] = v.[Code]);

UPDATE c SET [IsSimplified] = CASE WHEN d.[Workflow] = 'simple' THEN 1 ELSE 0 END
FROM [CommercialContract] c JOIN [ContractTypeDefinition] d ON d.[TenantId] = c.[TenantId] AND d.[Code] = c.[ContractType];");

            // 4) سطر بداية في سجل الانتقالات لكل عقد قائم (الخط الزمني يبدأ من الترحيل)
            migrationBuilder.Sql(@"
INSERT INTO [ContractStatusLog] ([Id], [TenantId], [CreatedAt], [ContractId], [FromStatus], [ToStatus], [UserName], [Note], [At])
SELECT NEWID(), c.[TenantId], SYSUTCDATETIME(), c.[Id], NULL, c.[Stage], N'system', N'ترحيل من نظام المراحل السابق', COALESCE(c.[UpdatedAt], c.[CreatedAt])
FROM [CommercialContract] c;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractApproval");

            migrationBuilder.DropTable(
                name: "ContractAttachment");

            migrationBuilder.DropTable(
                name: "ContractInvoice");

            migrationBuilder.DropTable(
                name: "ContractStatusLog");

            migrationBuilder.DropTable(
                name: "ContractTransition");

            migrationBuilder.DropTable(
                name: "ContractTypeDefinition");

            migrationBuilder.DeleteData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "active");

            migrationBuilder.DeleteData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "approval");

            migrationBuilder.DeleteData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "cancelled");

            migrationBuilder.DeleteData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "closed");

            migrationBuilder.DeleteData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "initial_acceptance");

            migrationBuilder.DeleteData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "review");

            migrationBuilder.DeleteData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "suspended");

            migrationBuilder.DropColumn(
                name: "Phase",
                table: "ContractStageDefinition");

            migrationBuilder.DropColumn(
                name: "ShortNameAr",
                table: "ContractStageDefinition");

            migrationBuilder.DropColumn(
                name: "ShortNameEn",
                table: "ContractStageDefinition");

            migrationBuilder.DropColumn(
                name: "SlaDays",
                table: "ContractStageDefinition");

            migrationBuilder.DropColumn(
                name: "AdvancePercent",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "GuaranteeAmount",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "GuaranteeBank",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "GuaranteeExpiry",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "GuaranteeNumber",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "IsSimplified",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "ReviewRound",
                table: "CommercialContract");

            migrationBuilder.DropColumn(
                name: "StageChangedAt",
                table: "CommercialContract");

            migrationBuilder.UpdateData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "draft",
                columns: new[] { "DescAr", "DescEn", "WorkflowType" },
                values: new object[] { "صياغة بنود التوريد والخدمات والاتفاقية والشروط الفنية", "Drafting supply and service clauses, the agreement, and technical terms", "general" });

            migrationBuilder.InsertData(
                table: "ContractStageDefinition",
                columns: new[] { "Id", "AdvanceActions", "AllowedActions", "BadgeColor", "DescAr", "DescEn", "HeaderColor", "Icon", "IsActive", "NameAr", "NameEn", "Number", "WorkflowType" },
                values: new object[,]
                {
                    { "active_execution", "edit", "edit,post,close", "bg-emerald-950 text-emerald-300 border-emerald-800", "إصدار أوامر العمل، تنفيذ التوريد، ومتابعة الجدول الزمني", "Issuing work orders, fulfilling supply, and tracking the schedule", "from-emerald-700 to-emerald-800", "engineering", true, "ساري وقيد التنفيذ", "Active Execution", 4, "general" },
                    { "approved_signed", "approve,sign", "approve,sign,reject", "bg-blue-950 text-blue-300 border-blue-800", "توقيع الأطراف، إيداع الضمانات الابتدائية، وتفعيل العقد", "Signing by the parties, depositing initial guarantees, and activating the contract", "from-blue-700 to-blue-800", "draw", true, "الاعتماد والتوقيع الرسمي", "Approval and Signing", 3, "general" },
                    { "final_closed", "", "", "bg-teal-950 text-teal-300 border-teal-800", "الإفراج عن ضمان حسن التنفيذ، التسوية الختامية والمخالصة", "Releasing the performance bond, final settlement, and clearance", "from-teal-700 to-teal-800", "verified", true, "الإغلاق والتسليم النهائي", "Final Handover and Closure", 7, "general" },
                    { "initial_inspection", "close", "edit,post,reject,close", "bg-cyan-950 text-cyan-300 border-cyan-800", "فحص مخرجات العقد، اعتماد محاضر الاستلام وإغلاق الملاحظات", "Inspecting contract deliverables, approving acceptance records, and closing remarks", "from-cyan-700 to-cyan-800", "checklist", true, "الفحص والاستلام الأولي", "Initial Inspection and Acceptance", 6, "general" },
                    { "legal_review", "review", "review,reject", "bg-amber-950 text-amber-300 border-amber-800", "تدقيق البنود، الشروط الجزائية، واعتماد الجدوى والمخاطر", "Reviewing clauses, penalties, feasibility, and risks", "from-amber-700 to-amber-800", "gavel", true, "المراجعة القانونية والمالية", "Legal and Financial Review", 2, "general" },
                    { "milestone_billing", "edit", "edit,post,close", "bg-purple-950 text-purple-300 border-purple-800", "إصدار فواتير المستخلصات الضريبية والقيود المحاسبية الآلية", "Issuing tax invoices for milestones and automated accounting entries", "from-purple-700 to-purple-800", "receipt_long", true, "المستخلصات والفوترة ZATCA", "Milestone Billing (ZATCA)", 5, "general" },
                    { "normal_agreement", "approve", "edit,delete,approve,post", "bg-indigo-950 text-indigo-300 border-indigo-800", "صياغة اتفاقية الشراء وتفاصيل البنود والكميات والأطراف", "Drafting the purchase agreement, line details, quantities, and parties", "from-indigo-700 to-indigo-800", "description", true, "اتفاقية شراء وتفاصيل الاتفاقية", "Purchase Agreement Details", 1, "normal_purchasing" },
                    { "normal_delivery", "edit", "edit,post", "bg-blue-950 text-blue-300 border-blue-800", "استلام المواد والخدمات وإصدار محضر وبيان التسليم", "Receiving goods and services and issuing a delivery receipt", "from-blue-700 to-blue-800", "local_shipping", true, "التسليم (بيان الاستلام)", "Delivery (Receipt Note)", 2, "normal_purchasing" },
                    { "normal_delivery_return", "edit", "edit,post", "bg-amber-950 text-amber-300 border-amber-800", "تسجيل المرتجعات والإشعارات المدينة المرتبطة ببيان التسليم", "Recording returns and debit notes linked to the delivery receipt", "from-amber-700 to-amber-800", "assignment_return", true, "مرتجع من بيان تسليم", "Delivery Return", 3, "normal_purchasing" },
                    { "normal_milestone_invoice", "", "post,close", "bg-emerald-950 text-emerald-300 border-emerald-800", "إصدار الفاتورة المرحلية وترحيل القيود المحاسبية التلقائية", "Issuing the milestone tax invoice and posting automated accounting entries", "from-emerald-700 to-emerald-800", "receipt_long", true, "فاتورة مرحلية (ضريبية ZATCA)", "Milestone Invoice (ZATCA Tax Invoice)", 4, "normal_purchasing" }
                });
        }
    }
}
