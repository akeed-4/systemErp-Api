using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPartyIsVatRegistered : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsVatRegistered",
                table: "Supplier",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsVatRegistered",
                table: "Customer",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // الأطراف القائمة برقم ضريبي تبقى «مسجَّلة» فلا يتغير نوع فواتيرها (كانت تُحدَّد بوجود الرقم)؛ الجديد افتراضه مبسطة
            migrationBuilder.Sql("UPDATE [Customer] SET [IsVatRegistered] = 1 WHERE [VatNumber] IS NOT NULL AND LTRIM(RTRIM([VatNumber])) <> ''");
            migrationBuilder.Sql("UPDATE [Supplier] SET [IsVatRegistered] = 1 WHERE [VatNumber] IS NOT NULL AND LTRIM(RTRIM([VatNumber])) <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsVatRegistered",
                table: "Supplier");

            migrationBuilder.DropColumn(
                name: "IsVatRegistered",
                table: "Customer");
        }
    }
}
