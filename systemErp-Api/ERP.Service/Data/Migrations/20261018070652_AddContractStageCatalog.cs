using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContractStageCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContractStageDefinition",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DescAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    DescEn = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Icon = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BadgeColor = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    HeaderColor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractStageDefinition", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "ContractStageDefinition",
                columns: new[] { "Id", "BadgeColor", "DescAr", "DescEn", "HeaderColor", "Icon", "IsActive", "NameAr", "NameEn", "Number", "WorkflowType" },
                values: new object[,]
                {
                    { "active_execution", "bg-emerald-950 text-emerald-300 border-emerald-800", "إصدار أوامر العمل، تنفيذ التوريد، ومتابعة الجدول الزمني", "Issuing work orders, fulfilling supply, and tracking the schedule", "from-emerald-700 to-emerald-800", "engineering", true, "ساري وقيد التنفيذ", "Active Execution", 4, "general" },
                    { "approved_signed", "bg-blue-950 text-blue-300 border-blue-800", "توقيع الأطراف، إيداع الضمانات الابتدائية، وتفعيل العقد", "Signing by the parties, depositing initial guarantees, and activating the contract", "from-blue-700 to-blue-800", "draw", true, "الاعتماد والتوقيع الرسمي", "Approval and Signing", 3, "general" },
                    { "draft", "bg-slate-700 text-slate-200 border-slate-600", "صياغة بنود التوريد والخدمات والاتفاقية والشروط الفنية", "Drafting supply and service clauses, the agreement, and technical terms", "from-slate-700 to-slate-800", "edit_document", true, "مسودة وصياغة العقد", "Contract Drafting", 1, "general" },
                    { "final_closed", "bg-teal-950 text-teal-300 border-teal-800", "الإفراج عن ضمان حسن التنفيذ، التسوية الختامية والمخالصة", "Releasing the performance bond, final settlement, and clearance", "from-teal-700 to-teal-800", "verified", true, "الإغلاق والتسليم النهائي", "Final Handover and Closure", 7, "general" },
                    { "initial_inspection", "bg-cyan-950 text-cyan-300 border-cyan-800", "فحص مخرجات العقد، اعتماد محاضر الاستلام وإغلاق الملاحظات", "Inspecting contract deliverables, approving acceptance records, and closing remarks", "from-cyan-700 to-cyan-800", "checklist", true, "الفحص والاستلام الأولي", "Initial Inspection and Acceptance", 6, "general" },
                    { "legal_review", "bg-amber-950 text-amber-300 border-amber-800", "تدقيق البنود، الشروط الجزائية، واعتماد الجدوى والمخاطر", "Reviewing clauses, penalties, feasibility, and risks", "from-amber-700 to-amber-800", "gavel", true, "المراجعة القانونية والمالية", "Legal and Financial Review", 2, "general" },
                    { "milestone_billing", "bg-purple-950 text-purple-300 border-purple-800", "إصدار فواتير المستخلصات الضريبية والقيود المحاسبية الآلية", "Issuing tax invoices for milestones and automated accounting entries", "from-purple-700 to-purple-800", "receipt_long", true, "المستخلصات والفوترة ZATCA", "Milestone Billing (ZATCA)", 5, "general" },
                    { "normal_agreement", "bg-indigo-950 text-indigo-300 border-indigo-800", "صياغة اتفاقية الشراء وتفاصيل البنود والكميات والأطراف", "Drafting the purchase agreement, line details, quantities, and parties", "from-indigo-700 to-indigo-800", "description", true, "اتفاقية شراء وتفاصيل الاتفاقية", "Purchase Agreement Details", 1, "normal_purchasing" },
                    { "normal_delivery", "bg-blue-950 text-blue-300 border-blue-800", "استلام المواد والخدمات وإصدار محضر وبيان التسليم", "Receiving goods and services and issuing a delivery receipt", "from-blue-700 to-blue-800", "local_shipping", true, "التسليم (بيان الاستلام)", "Delivery (Receipt Note)", 2, "normal_purchasing" },
                    { "normal_delivery_return", "bg-amber-950 text-amber-300 border-amber-800", "تسجيل المرتجعات والإشعارات المدينة المرتبطة ببيان التسليم", "Recording returns and debit notes linked to the delivery receipt", "from-amber-700 to-amber-800", "assignment_return", true, "مرتجع من بيان تسليم", "Delivery Return", 3, "normal_purchasing" },
                    { "normal_milestone_invoice", "bg-emerald-950 text-emerald-300 border-emerald-800", "إصدار الفاتورة المرحلية وترحيل القيود المحاسبية التلقائية", "Issuing the milestone tax invoice and posting automated accounting entries", "from-emerald-700 to-emerald-800", "receipt_long", true, "فاتورة مرحلية (ضريبية ZATCA)", "Milestone Invoice (ZATCA Tax Invoice)", 4, "normal_purchasing" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContractStageDefinition_WorkflowType_Number",
                table: "ContractStageDefinition",
                columns: new[] { "WorkflowType", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractStageDefinition");
        }
    }
}
