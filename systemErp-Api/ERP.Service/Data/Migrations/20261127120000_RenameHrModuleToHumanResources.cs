using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>تسمية وحدة hr في دليل الوحدات (الباقات والاشتراكات) «الموارد البشرية» بدل «شؤون الموظفين». الاسم فقط.</summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261127120000_RenameHrModuleToHumanResources")]
    public partial class RenameHrModuleToHumanResources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [PlatformModuleDefinition] SET [NameAr] = N'الموارد البشرية' WHERE [Key] = N'hr';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [PlatformModuleDefinition] SET [NameAr] = N'شؤون الموظفين' WHERE [Key] = N'hr';");
        }
    }
}
