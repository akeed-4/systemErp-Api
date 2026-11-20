using System;
using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// تأجير المعدات للعملاء (R1): فئات التأجير وأسعارها، والحجوزات بأسطرها، وثلاثة أعمدة على سجل المعدات القائم (قابلة للتأجير
    /// افتراضياً لا، والفئة والموقع فارغان). لا يتغير شيء لمعدات المقاولات القائمة.
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261120230000_RentalCatalogAndBookings")]
    public partial class RentalCatalogAndBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RentalCategory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RentalCategory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RentalCategory_RentalCategory_ParentId",
                        column: x => x.ParentId,
                        principalTable: "RentalCategory",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddColumn<bool>(name: "IsRentable", table: "Equipment", type: "bit", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<Guid>(name: "RentalCategoryId", table: "Equipment", type: "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "HomeWarehouseId", table: "Equipment", type: "uniqueidentifier", nullable: true);

            migrationBuilder.CreateTable(
                name: "RentalRate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Basis = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OperatorRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    IncludedHours = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OvertimeRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MinimumPeriods = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RentalRate", x => x.Id);
                    table.ForeignKey(name: "FK_RentalRate_Equipment_EquipmentId", column: x => x.EquipmentId, principalTable: "Equipment",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_RentalRate_RentalCategory_CategoryId", column: x => x.CategoryId, principalTable: "RentalCategory",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RentalBooking",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    HoldUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RentalBooking", x => x.Id);
                    table.ForeignKey(name: "FK_RentalBooking_Customer_CustomerId", column: x => x.CustomerId, principalTable: "Customer",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_RentalBooking_Warehouse_WarehouseId", column: x => x.WarehouseId, principalTable: "Warehouse",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RentalBookingLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    From = table.Column<DateTime>(type: "datetime2", nullable: false),
                    To = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Basis = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    WithOperator = table.Column<bool>(type: "bit", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OperatorRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    EstimatedAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RentalBookingLine", x => x.Id);
                    table.ForeignKey(name: "FK_RentalBookingLine_Equipment_EquipmentId", column: x => x.EquipmentId, principalTable: "Equipment",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_RentalBookingLine_RentalBooking_BookingId", column: x => x.BookingId, principalTable: "RentalBooking",
                        principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_RentalBookingLine_RentalCategory_CategoryId", column: x => x.CategoryId, principalTable: "RentalCategory",
                        principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(name: "IX_RentalCategory_ParentId", table: "RentalCategory", column: "ParentId");
            migrationBuilder.CreateIndex(name: "IX_RentalCategory_TenantId", table: "RentalCategory", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_RentalCategory_TenantId_Code", table: "RentalCategory", columns: new[] { "TenantId", "Code" }, unique: true);

            migrationBuilder.CreateIndex(name: "IX_Equipment_HomeWarehouseId", table: "Equipment", column: "HomeWarehouseId");
            migrationBuilder.CreateIndex(name: "IX_Equipment_RentalCategoryId", table: "Equipment", column: "RentalCategoryId");

            migrationBuilder.CreateIndex(name: "IX_RentalRate_CategoryId", table: "RentalRate", column: "CategoryId");
            migrationBuilder.CreateIndex(name: "IX_RentalRate_EquipmentId", table: "RentalRate", column: "EquipmentId");
            migrationBuilder.CreateIndex(name: "IX_RentalRate_TenantId", table: "RentalRate", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_RentalRate_TenantId_CategoryId_Basis", table: "RentalRate", columns: new[] { "TenantId", "CategoryId", "Basis" });
            migrationBuilder.CreateIndex(name: "IX_RentalRate_TenantId_EquipmentId_Basis", table: "RentalRate", columns: new[] { "TenantId", "EquipmentId", "Basis" });

            migrationBuilder.CreateIndex(name: "IX_RentalBooking_CustomerId", table: "RentalBooking", column: "CustomerId");
            migrationBuilder.CreateIndex(name: "IX_RentalBooking_TenantId", table: "RentalBooking", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_RentalBooking_WarehouseId", table: "RentalBooking", column: "WarehouseId");
            migrationBuilder.CreateIndex(name: "IX_RentalBooking_TenantId_BookingNumber", table: "RentalBooking", columns: new[] { "TenantId", "BookingNumber" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_RentalBooking_TenantId_Status", table: "RentalBooking", columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(name: "IX_RentalBookingLine_BookingId", table: "RentalBookingLine", column: "BookingId");
            migrationBuilder.CreateIndex(name: "IX_RentalBookingLine_CategoryId", table: "RentalBookingLine", column: "CategoryId");
            migrationBuilder.CreateIndex(name: "IX_RentalBookingLine_EquipmentId", table: "RentalBookingLine", column: "EquipmentId");
            migrationBuilder.CreateIndex(name: "IX_RentalBookingLine_TenantId", table: "RentalBookingLine", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_RentalBookingLine_TenantId_EquipmentId_From_To", table: "RentalBookingLine",
                columns: new[] { "TenantId", "EquipmentId", "From", "To" });

            migrationBuilder.AddForeignKey(name: "FK_Equipment_RentalCategory_RentalCategoryId", table: "Equipment", column: "RentalCategoryId",
                principalTable: "RentalCategory", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_Equipment_Warehouse_HomeWarehouseId", table: "Equipment", column: "HomeWarehouseId",
                principalTable: "Warehouse", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_Equipment_RentalCategory_RentalCategoryId", table: "Equipment");
            migrationBuilder.DropForeignKey(name: "FK_Equipment_Warehouse_HomeWarehouseId", table: "Equipment");
            migrationBuilder.DropTable(name: "RentalBookingLine");
            migrationBuilder.DropTable(name: "RentalBooking");
            migrationBuilder.DropTable(name: "RentalRate");
            migrationBuilder.DropIndex(name: "IX_Equipment_HomeWarehouseId", table: "Equipment");
            migrationBuilder.DropIndex(name: "IX_Equipment_RentalCategoryId", table: "Equipment");
            migrationBuilder.DropColumn(name: "IsRentable", table: "Equipment");
            migrationBuilder.DropColumn(name: "RentalCategoryId", table: "Equipment");
            migrationBuilder.DropColumn(name: "HomeWarehouseId", table: "Equipment");
            migrationBuilder.DropTable(name: "RentalCategory");
        }
    }
}
