using System;
using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// ورش السيارات W2: فحوص أمر العمل ونتائجها، وأسطره، ونسخ التقديرات وأسطرها بقرار العميل، واستثناءات التنفيذ الموثقة. جداول جديدة فقط.
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261121130000_WorkshopLinesEstimates")]
    public partial class WorkshopLinesEstimates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkOrderInspection",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Usage = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    TemplateName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    InspectorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    InspectorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderInspection", x => x.Id);
                    table.ForeignKey(name: "FK_WorkOrderInspection_InspectionTemplate_TemplateId", column: x => x.TemplateId, principalTable: "InspectionTemplate",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkOrderInspection_WorkOrder_WorkOrderId", column: x => x.WorkOrderId, principalTable: "WorkOrder",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderInspectionResult",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InspectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Section = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TextAr = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    TextEn = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ResultType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PhotoRequiredOnFail = table.Column<bool>(type: "bit", nullable: false),
                    Condition = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    NumberValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    TextValue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderInspectionResult", x => x.Id);
                    table.ForeignKey(name: "FK_WorkOrderInspectionResult_WorkOrderInspection_InspectionId", column: x => x.InspectionId, principalTable: "WorkOrderInspection",
                        principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderEstimate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DiscountTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RespondedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResponseChannel = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    RespondedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ResponseNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RecordedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderEstimate", x => x.Id);
                    table.ForeignKey(name: "FK_WorkOrderEstimate_WorkOrder_WorkOrderId", column: x => x.WorkOrderId, principalTable: "WorkOrder",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PriceOverridden = table.Column<bool>(type: "bit", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ApprovalStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ApprovedEstimateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BillingType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ExecutionStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    EstimatedCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    AddedAfterApproval = table.Column<bool>(type: "bit", nullable: false),
                    SourceResultId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderLine", x => x.Id);
                    table.ForeignKey(name: "FK_WorkOrderLine_Product_ProductId", column: x => x.ProductId, principalTable: "Product",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkOrderLine_WorkOrderEstimate_ApprovedEstimateId", column: x => x.ApprovedEstimateId, principalTable: "WorkOrderEstimate",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkOrderLine_WorkOrderInspectionResult_SourceResultId", column: x => x.SourceResultId, principalTable: "WorkOrderInspectionResult",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkOrderLine_WorkOrder_WorkOrderId", column: x => x.WorkOrderId, principalTable: "WorkOrder",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkOrderLine_WorkshopServiceDefinition_ServiceId", column: x => x.ServiceId, principalTable: "WorkshopServiceDefinition",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderEstimateLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EstimateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BillingType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderEstimateLine", x => x.Id);
                    table.ForeignKey(name: "FK_WorkOrderEstimateLine_WorkOrderEstimate_EstimateId", column: x => x.EstimateId, principalTable: "WorkOrderEstimate",
                        principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_WorkOrderEstimateLine_WorkOrderLine_LineId", column: x => x.LineId, principalTable: "WorkOrderLine",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderExecutionException",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    AttachmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AuthorizedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AuthorizedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AuthorizedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderExecutionException", x => x.Id);
                    table.ForeignKey(name: "FK_WorkOrderExecutionException_WorkOrderLine_LineId", column: x => x.LineId, principalTable: "WorkOrderLine",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_WorkOrderExecutionException_WorkOrder_WorkOrderId", column: x => x.WorkOrderId, principalTable: "WorkOrder",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(name: "IX_WorkOrderInspection_TemplateId", table: "WorkOrderInspection", column: "TemplateId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderInspection_TenantId", table: "WorkOrderInspection", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderInspection_WorkOrderId", table: "WorkOrderInspection", column: "WorkOrderId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderInspection_TenantId_WorkOrderId", table: "WorkOrderInspection", columns: new[] { "TenantId", "WorkOrderId" });
            migrationBuilder.CreateIndex(name: "IX_WorkOrderInspectionResult_InspectionId", table: "WorkOrderInspectionResult", column: "InspectionId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderInspectionResult_TenantId", table: "WorkOrderInspectionResult", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderEstimate_TenantId", table: "WorkOrderEstimate", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderEstimate_WorkOrderId", table: "WorkOrderEstimate", column: "WorkOrderId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderEstimate_TenantId_WorkOrderId_Version", table: "WorkOrderEstimate", columns: new[] { "TenantId", "WorkOrderId", "Version" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_WorkOrderLine_ApprovedEstimateId", table: "WorkOrderLine", column: "ApprovedEstimateId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderLine_ProductId", table: "WorkOrderLine", column: "ProductId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderLine_ServiceId", table: "WorkOrderLine", column: "ServiceId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderLine_SourceResultId", table: "WorkOrderLine", column: "SourceResultId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderLine_TenantId", table: "WorkOrderLine", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderLine_WorkOrderId", table: "WorkOrderLine", column: "WorkOrderId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderLine_TenantId_WorkOrderId", table: "WorkOrderLine", columns: new[] { "TenantId", "WorkOrderId" });
            migrationBuilder.CreateIndex(name: "IX_WorkOrderEstimateLine_EstimateId", table: "WorkOrderEstimateLine", column: "EstimateId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderEstimateLine_LineId", table: "WorkOrderEstimateLine", column: "LineId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderEstimateLine_TenantId", table: "WorkOrderEstimateLine", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderExecutionException_LineId", table: "WorkOrderExecutionException", column: "LineId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderExecutionException_TenantId", table: "WorkOrderExecutionException", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderExecutionException_WorkOrderId", table: "WorkOrderExecutionException", column: "WorkOrderId");
            migrationBuilder.CreateIndex(name: "IX_WorkOrderExecutionException_TenantId_LineId", table: "WorkOrderExecutionException", columns: new[] { "TenantId", "LineId" }, unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WorkOrderExecutionException");
            migrationBuilder.DropTable(name: "WorkOrderEstimateLine");
            migrationBuilder.DropTable(name: "WorkOrderLine");
            migrationBuilder.DropTable(name: "WorkOrderEstimate");
            migrationBuilder.DropTable(name: "WorkOrderInspectionResult");
            migrationBuilder.DropTable(name: "WorkOrderInspection");
        }
    }
}
