using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCarStageCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CarStageDefinition",
                columns: table => new
                {
                    WorkflowType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DescAr = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    DescEn = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Icon = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Color = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarStageDefinition", x => new { x.WorkflowType, x.Id });
                });

            migrationBuilder.InsertData(
                table: "CarStageDefinition",
                columns: new[] { "Id", "WorkflowType", "Color", "DescAr", "DescEn", "Icon", "IsActive", "NameAr", "NameEn", "Number" },
                values: new object[,]
                {
                    { "invoiced", "procurement", "teal", "استلام فاتورة المورد الضريبية ومطابقتها وترحيل القيد المحاسبي ومرتجع الشراء.", "Receiving and matching the supplier tax invoice, posting the journal entry, and purchase returns.", "receipt_long", true, "الفوترة النهائية وإقفال الدورة", "Invoicing & Closing", 7 },
                    { "purchase_order", "procurement", "cyan", "إصدار أمر الشراء الرسمي الملزم قانونياً للوكيل مع تحديد شروط الدفع والتسليم.", "Issuing the legally binding purchase order to the agent with payment and delivery terms.", "shopping_cart_checkout", true, "أمر الشراء الرسمي (PO)", "Purchase Order (PO)", 5 },
                    { "requisition", "procurement", "indigo", "تسجيل وتحديد الاحتياج من ماركات وموديلات السيارات لتغذية صالة العرض والمخازن.", "Recording the required car brands and models to stock the showroom and warehouses.", "post_add", true, "طلب بضاعة سيارات", "Requisition", 1 },
                    { "requisition_approved", "procurement", "blue", "المراجعة الإدارية والمالية وتدقيق الميزانية المخصصة وتوقيع اعتماد طلب الشراء.", "Administrative and financial review, budget check, and sign-off of the purchase request.", "verified", true, "تدقيق واعتماد طلب البضاعة", "Requisition Approval", 2 },
                    { "rfq", "procurement", "purple", "إرسال واستقبال طلبات التسعير من الوكلاء المعتمدين والموزعين للمفاضلة التنافسية.", "Sending and receiving price requests from authorized agents and distributors for comparison.", "request_quote", true, "عروض أسعار الموردين (RFQ)", "Supplier Quotations (RFQ)", 3 },
                    { "rfq_approved", "procurement", "amber", "اختيار العرض الأنسب سعراً ومواصفات وتوقيع قرار الاعتماد.", "Selecting the best offer on price and specifications and signing the approval.", "published_with_changes", true, "فحص واعتماد عرض السعر المقبول", "Quotation Approval", 4 },
                    { "vin_received", "procurement", "emerald", "استلام السيارات فعلياً ومطابقة أرقام الشواسي والمحركات والبطاقات الجمركية وإدخال المخزن.", "Physically receiving the cars, matching VINs, engine numbers, and customs cards, and stocking them.", "local_shipping", true, "استلام الشواسي VIN وإدخال المخزن", "VIN Receiving & Inspection", 6 },
                    { "allocation", "sales_contract", "blue", "ربط العقد المعتمد برقم شاسي VIN محدد من المستودع، وبدء فحص الجودة والتجهيز الفني PDI.", "Linking the approved contract to a specific VIN from the warehouse and starting the PDI inspection.", "pin", true, "تخصيص الشواسي وتجهيز PDI", "VIN Allocation & PDI", 3 },
                    { "approvals", "sales_contract", "indigo", "المراجعة الائتمانية وتدقيق المستندات والموافقة البنكية أو سداد الدفعة الأولى واعتماد العقد رسمياً.", "Credit review, document checks, bank approval or down payment, and formal contract approval.", "verified", true, "تدقيق واعتماد العقود", "Contract Approvals", 2 },
                    { "delivery", "sales_contract", "emerald", "تسليم السيارة للمشتري رسمياً وتوقيع محضر الاستلام والفحص النهائي واستلام المفاتيح.", "Formally handing the car to the buyer, signing the handover record, final inspection, and key handover.", "how_to_reg", true, "تسليم المركبة ومحضر الاستلام", "Delivery & Handover", 4 },
                    { "drafts", "sales_contract", "slate", "إنشاء طلب بيع سيارة جديد، تسجيل بيانات المشتري، واختيار نوع دورة البيع (نقدي / تأجيري / شركات).", "Creating a new car sale request, recording buyer details, and choosing the sales cycle (cash / lease / corporate).", "post_add", true, "طلبات ومسودات العقود", "Contract Drafts", 1 },
                    { "invoicing", "sales_contract", "purple", "إصدار الفاتورة الضريبية النهائية وتطبيق ضريبة القيمة المضافة (15% أو هامش الربح) وترحيل القيد.", "Issuing the final tax invoice, applying VAT (15% or margin scheme), and posting the journal entry.", "receipt_long", true, "الفوترة والترحيل المالي", "Invoicing & Financial Posting", 5 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CarStageDefinition_WorkflowType_Number",
                table: "CarStageDefinition",
                columns: new[] { "WorkflowType", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CarStageDefinition");
        }
    }
}
