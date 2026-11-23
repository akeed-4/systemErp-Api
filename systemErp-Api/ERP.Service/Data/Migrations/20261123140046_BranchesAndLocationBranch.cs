using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// المرحلة الأولى من هيكل «النشاط ← الفرع ← الموقع»: جدول الفروع، وربط كل موقع (Warehouse) بفرع من نشاطه.
    /// ترحيل البيانات: لكل نشاط له مواقع في هذه القاعدة «الفرع الرئيسي» افتراضياً تُلحق به مواقعه كلها. لا يمس الحسابات
    /// ولا القيود ولا الأرصدة: عمود جديد يُملأ ثم يصبح إلزامياً، ويتوقف الترحيل إن بقي موقع بلا فرع.
    /// </summary>
    public partial class BranchesAndLocationBranch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Branch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    City = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Address = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ManagerName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branch", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Branch_TenantId",
                table: "Branch",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Branch_TenantId_Code",
                table: "Branch",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            // الفرع الرئيسي لكل نشاط له مواقع في هذه القاعدة (المشتركة أو المستقلة)؛ النشاط بلا مواقع يُنشأ فرعه عند أول موقع
            migrationBuilder.Sql(@"
INSERT INTO [Branch] ([Id], [Code], [NameAr], [NameEn], [IsDefault], [Status], [TenantId], [CreatedAt])
SELECT NEWID(), N'BR-001', N'الفرع الرئيسي', N'Main Branch', 1, N'active', t.[TenantId], SYSUTCDATETIME()
FROM (SELECT DISTINCT [TenantId] FROM [Warehouse]) AS t
WHERE NOT EXISTS (SELECT 1 FROM [Branch] b WHERE b.[TenantId] = t.[TenantId]);");

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "Warehouse",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql(@"
UPDATE w SET w.[BranchId] = b.[Id]
FROM [Warehouse] w
INNER JOIN [Branch] b ON b.[TenantId] = w.[TenantId] AND b.[IsDefault] = 1
WHERE w.[BranchId] IS NULL;");

            // فحص قبل جعل العمود إلزامياً: لا موقع بلا فرع، ولا فرع من نشاط آخر
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [Warehouse] WHERE [BranchId] IS NULL)
    THROW 50001, N'BranchesAndLocationBranch: location without branch after backfill.', 1;
IF EXISTS (SELECT 1 FROM [Warehouse] w INNER JOIN [Branch] b ON b.[Id] = w.[BranchId] WHERE b.[TenantId] <> w.[TenantId])
    THROW 50002, N'BranchesAndLocationBranch: location linked to a branch of another tenant.', 1;");

            migrationBuilder.AlterColumn<Guid>(
                name: "BranchId",
                table: "Warehouse",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Warehouse_BranchId",
                table: "Warehouse",
                column: "BranchId");

            migrationBuilder.AddForeignKey(
                name: "FK_Warehouse_Branch_BranchId",
                table: "Warehouse",
                column: "BranchId",
                principalTable: "Branch",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Warehouse_Branch_BranchId",
                table: "Warehouse");

            migrationBuilder.DropIndex(
                name: "IX_Warehouse_BranchId",
                table: "Warehouse");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "Warehouse");

            migrationBuilder.DropTable(
                name: "Branch");
        }
    }
}
