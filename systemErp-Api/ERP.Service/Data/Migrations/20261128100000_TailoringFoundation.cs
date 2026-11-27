using System;
using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// الخياطة والتفصيل — المرحلة 1: الإضافة في دليل الوحدات (لا تُمنح لأحد)، والإعداد وأنواع الملابس بحقول قياسها وخيارات تصميمها،
    /// ومراحل الإنتاج، وامتداد العميل وملفات القياس ونسخها الثابتة. جداول جديدة فقط؛ الافتراضيات تُزرع عند أول فتح للوحدة لا هنا.
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261128100000_TailoringFoundation")]
    public partial class TailoringFoundation : Migration
    {
        private static readonly string[] ModuleColumns = { "Key", "DescriptionAr", "DescriptionEn", "Icon", "IsActive", "NameAr", "NameEn", "Number", "SortOrder" };
        private static readonly string[] ModuleColumnTypes = { "nvarchar(64)", "nvarchar(max)", "nvarchar(max)", "nvarchar(max)", "bit", "nvarchar(200)", "nvarchar(200)", "int", "int" };

        private static OperationBuilder<AddColumnOperation> Str(ColumnsBuilder t, int max, bool required = false)
            => t.Column<string>(type: $"nvarchar({max})", maxLength: max, nullable: !required);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(table: "PlatformModuleDefinition", columns: ModuleColumns, columnTypes: ModuleColumnTypes,
                values: new object[] { "tailoring", "قياسات العملاء وتصاميم الملابس وأوامر التفصيل ومراحل الإنتاج والبروفات والأقمشة وأجور الخياطين والتسليم.",
                    "Customer measurements, garment designs, tailoring orders, production stages, fittings, fabrics, tailor labor and delivery.",
                    "checkroom", true, "الخياطة والتفصيل", "Tailoring & Garments", 9, 9 });

            migrationBuilder.CreateTable(
                name: "TailoringSettings",
                columns: t => new
                {
                    Id = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefaultWarehouseId = t.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DefaultLeadDays = t.Column<int>(type: "int", nullable: false),
                    MinAdvancePercent = t.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    RequireQualityCheck = t.Column<bool>(type: "bit", nullable: false),
                    TenantId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = t.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = t.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_TailoringSettings", x => x.Id);
                    t.ForeignKey("FK_TailoringSettings_Warehouse_DefaultWarehouseId", x => x.DefaultWarehouseId, "Warehouse", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TailoringGarmentType",
                columns: t => new
                {
                    Id = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = Str(t, 32, true),
                    NameAr = Str(t, 150, true),
                    NameEn = Str(t, 150),
                    Category = Str(t, 16, true),
                    ProductId = t.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BasePrice = t.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DefaultFabricMeters = t.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    MeasurementUnit = Str(t, 8, true),
                    SortOrder = t.Column<int>(type: "int", nullable: false),
                    IsActive = t.Column<bool>(type: "bit", nullable: false),
                    Notes = Str(t, 500),
                    TenantId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = t.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = t.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_TailoringGarmentType", x => x.Id);
                    t.ForeignKey("FK_TailoringGarmentType_Product_ProductId", x => x.ProductId, "Product", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TailoringMeasurementField",
                columns: t => new
                {
                    Id = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GarmentTypeId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = Str(t, 48, true),
                    NameAr = Str(t, 150, true),
                    NameEn = Str(t, 150),
                    GroupName = Str(t, 100),
                    SortOrder = t.Column<int>(type: "int", nullable: false),
                    IsRequired = t.Column<bool>(type: "bit", nullable: false),
                    MinValue = t.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    MaxValue = t.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    IsActive = t.Column<bool>(type: "bit", nullable: false),
                    TenantId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = t.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = t.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_TailoringMeasurementField", x => x.Id);
                    t.ForeignKey("FK_TailoringMeasurementField_TailoringGarmentType_GarmentTypeId", x => x.GarmentTypeId, "TailoringGarmentType", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TailoringDesignOption",
                columns: t => new
                {
                    Id = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GarmentTypeId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = Str(t, 48, true),
                    NameAr = Str(t, 150, true),
                    NameEn = Str(t, 150),
                    InputType = Str(t, 16, true),
                    IsRequired = t.Column<bool>(type: "bit", nullable: false),
                    SortOrder = t.Column<int>(type: "int", nullable: false),
                    IsActive = t.Column<bool>(type: "bit", nullable: false),
                    TenantId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = t.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = t.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_TailoringDesignOption", x => x.Id);
                    t.ForeignKey("FK_TailoringDesignOption_TailoringGarmentType_GarmentTypeId", x => x.GarmentTypeId, "TailoringGarmentType", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TailoringDesignOptionValue",
                columns: t => new
                {
                    Id = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OptionId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = Str(t, 48, true),
                    NameAr = Str(t, 150, true),
                    NameEn = Str(t, 150),
                    PriceDelta = t.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SortOrder = t.Column<int>(type: "int", nullable: false),
                    IsDefault = t.Column<bool>(type: "bit", nullable: false),
                    IsActive = t.Column<bool>(type: "bit", nullable: false),
                    TenantId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = t.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = t.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_TailoringDesignOptionValue", x => x.Id);
                    t.ForeignKey("FK_TailoringDesignOptionValue_TailoringDesignOption_OptionId", x => x.OptionId, "TailoringDesignOption", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TailoringStage",
                columns: t => new
                {
                    Id = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = Str(t, 32, true),
                    NameAr = Str(t, 150, true),
                    NameEn = Str(t, 150),
                    SortOrder = t.Column<int>(type: "int", nullable: false),
                    IsMandatory = t.Column<bool>(type: "bit", nullable: false),
                    IsQualityCheck = t.Column<bool>(type: "bit", nullable: false),
                    DefaultPieceRate = t.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GarmentTypeIds = Str(t, 2000),
                    IsActive = t.Column<bool>(type: "bit", nullable: false),
                    TenantId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = t.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = t.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: t => t.PrimaryKey("PK_TailoringStage", x => x.Id));

            migrationBuilder.CreateTable(
                name: "TailoringCustomerProfile",
                columns: t => new
                {
                    Id = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Preferences = Str(t, 2000),
                    Notes = Str(t, 2000),
                    TenantId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = t.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = t.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_TailoringCustomerProfile", x => x.Id);
                    t.ForeignKey("FK_TailoringCustomerProfile_Customer_CustomerId", x => x.CustomerId, "Customer", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TailoringMeasurementProfile",
                columns: t => new
                {
                    Id = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GarmentTypeId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WearerName = Str(t, 150, true),
                    Relation = Str(t, 50),
                    CurrentVersionNo = t.Column<int>(type: "int", nullable: false),
                    CurrentVersionId = t.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = t.Column<bool>(type: "bit", nullable: false),
                    Notes = Str(t, 500),
                    TenantId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = t.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = t.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_TailoringMeasurementProfile", x => x.Id);
                    t.ForeignKey("FK_TailoringMeasurementProfile_Customer_CustomerId", x => x.CustomerId, "Customer", "Id", onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_TailoringMeasurementProfile_TailoringGarmentType_GarmentTypeId", x => x.GarmentTypeId, "TailoringGarmentType", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TailoringMeasurementVersion",
                columns: t => new
                {
                    Id = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProfileId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNo = t.Column<int>(type: "int", nullable: false),
                    MeasuredAt = t.Column<DateTime>(type: "datetime2", nullable: false),
                    MeasuredByEmployeeId = t.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MeasuredByName = Str(t, 200),
                    Unit = Str(t, 8, true),
                    ValuesJson = t.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = Str(t, 1000),
                    CreatedBy = Str(t, 200),
                    TenantId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = t.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = t.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: t =>
                {
                    t.PrimaryKey("PK_TailoringMeasurementVersion", x => x.Id);
                    t.ForeignKey("FK_TailoringMeasurementVersion_TailoringMeasurementProfile_ProfileId", x => x.ProfileId, "TailoringMeasurementProfile", "Id", onDelete: ReferentialAction.Restrict);
                    t.ForeignKey("FK_TailoringMeasurementVersion_Employee_MeasuredByEmployeeId", x => x.MeasuredByEmployeeId, "Employee", "Id", onDelete: ReferentialAction.Restrict);
                });

            foreach (var table in Tables) migrationBuilder.CreateIndex($"IX_{table}_TenantId", table, "TenantId");
            migrationBuilder.CreateIndex("IX_TailoringSettings_DefaultWarehouseId", "TailoringSettings", "DefaultWarehouseId");
            migrationBuilder.CreateIndex("IX_TailoringGarmentType_ProductId", "TailoringGarmentType", "ProductId");
            migrationBuilder.CreateIndex("IX_TailoringGarmentType_TenantId_Code", "TailoringGarmentType", new[] { "TenantId", "Code" }, unique: true);
            migrationBuilder.CreateIndex("IX_TailoringMeasurementField_GarmentTypeId", "TailoringMeasurementField", "GarmentTypeId");
            migrationBuilder.CreateIndex("IX_TailoringMeasurementField_TenantId_GarmentTypeId_Key", "TailoringMeasurementField", new[] { "TenantId", "GarmentTypeId", "Key" }, unique: true);
            migrationBuilder.CreateIndex("IX_TailoringDesignOption_GarmentTypeId", "TailoringDesignOption", "GarmentTypeId");
            migrationBuilder.CreateIndex("IX_TailoringDesignOption_TenantId_GarmentTypeId_Key", "TailoringDesignOption", new[] { "TenantId", "GarmentTypeId", "Key" }, unique: true);
            migrationBuilder.CreateIndex("IX_TailoringDesignOptionValue_OptionId", "TailoringDesignOptionValue", "OptionId");
            migrationBuilder.CreateIndex("IX_TailoringDesignOptionValue_TenantId_OptionId_Code", "TailoringDesignOptionValue", new[] { "TenantId", "OptionId", "Code" }, unique: true);
            migrationBuilder.CreateIndex("IX_TailoringStage_TenantId_Code", "TailoringStage", new[] { "TenantId", "Code" }, unique: true);
            migrationBuilder.CreateIndex("IX_TailoringCustomerProfile_CustomerId", "TailoringCustomerProfile", "CustomerId");
            migrationBuilder.CreateIndex("IX_TailoringCustomerProfile_TenantId_CustomerId", "TailoringCustomerProfile", new[] { "TenantId", "CustomerId" }, unique: true);
            migrationBuilder.CreateIndex("IX_TailoringMeasurementProfile_CustomerId", "TailoringMeasurementProfile", "CustomerId");
            migrationBuilder.CreateIndex("IX_TailoringMeasurementProfile_GarmentTypeId", "TailoringMeasurementProfile", "GarmentTypeId");
            migrationBuilder.CreateIndex("IX_TailoringMeasurementProfile_TenantId_CustomerId", "TailoringMeasurementProfile", new[] { "TenantId", "CustomerId" });
            migrationBuilder.CreateIndex("IX_TailoringMeasurementVersion_MeasuredByEmployeeId", "TailoringMeasurementVersion", "MeasuredByEmployeeId");
            migrationBuilder.CreateIndex("IX_TailoringMeasurementVersion_ProfileId", "TailoringMeasurementVersion", "ProfileId");
            migrationBuilder.CreateIndex("IX_TailoringMeasurementVersion_TenantId_ProfileId_VersionNo", "TailoringMeasurementVersion", new[] { "TenantId", "ProfileId", "VersionNo" }, unique: true);
        }

        private static readonly string[] Tables =
        {
            "TailoringSettings", "TailoringGarmentType", "TailoringMeasurementField", "TailoringDesignOption", "TailoringDesignOptionValue",
            "TailoringStage", "TailoringCustomerProfile", "TailoringMeasurementProfile", "TailoringMeasurementVersion",
        };

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // الأبناء قبل الآباء
            foreach (var table in new[]
            {
                "TailoringMeasurementVersion", "TailoringMeasurementProfile", "TailoringCustomerProfile", "TailoringStage",
                "TailoringDesignOptionValue", "TailoringDesignOption", "TailoringMeasurementField", "TailoringGarmentType", "TailoringSettings",
            })
                migrationBuilder.DropTable(table);

            migrationBuilder.Sql("UPDATE [Subscription] SET ModuleKeys = REPLACE(ModuleKeys, ',tailoring', '') WHERE ModuleKeys LIKE '%,tailoring%';");
            migrationBuilder.DeleteData(table: "PlatformModuleDefinition", keyColumn: "Key", keyColumnType: "nvarchar(64)", keyValue: "tailoring");
        }
    }
}
