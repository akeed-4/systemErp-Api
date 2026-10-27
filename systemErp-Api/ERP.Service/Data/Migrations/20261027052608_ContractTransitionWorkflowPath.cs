using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ContractTransitionWorkflowPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Workflow",
                table: "ContractTransition",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "ContractTransition",
                keyColumn: "Id",
                keyValue: 1,
                column: "Workflow",
                value: "full");

            migrationBuilder.UpdateData(
                table: "ContractTransition",
                keyColumn: "Id",
                keyValue: 2,
                column: "Workflow",
                value: null);

            migrationBuilder.UpdateData(
                table: "ContractTransition",
                keyColumn: "Id",
                keyValue: 3,
                column: "Workflow",
                value: null);

            migrationBuilder.UpdateData(
                table: "ContractTransition",
                keyColumn: "Id",
                keyValue: 4,
                column: "Workflow",
                value: null);

            migrationBuilder.UpdateData(
                table: "ContractTransition",
                keyColumn: "Id",
                keyValue: 5,
                column: "Workflow",
                value: null);

            migrationBuilder.UpdateData(
                table: "ContractTransition",
                keyColumn: "Id",
                keyValue: 6,
                column: "Workflow",
                value: null);

            migrationBuilder.UpdateData(
                table: "ContractTransition",
                keyColumn: "Id",
                keyValue: 7,
                column: "Workflow",
                value: null);

            migrationBuilder.UpdateData(
                table: "ContractTransition",
                keyColumn: "Id",
                keyValue: 8,
                column: "Workflow",
                value: null);

            migrationBuilder.UpdateData(
                table: "ContractTransition",
                keyColumn: "Id",
                keyValue: 9,
                column: "Workflow",
                value: null);

            migrationBuilder.UpdateData(
                table: "ContractTransition",
                keyColumn: "Id",
                keyValue: 10,
                column: "Workflow",
                value: null);

            migrationBuilder.UpdateData(
                table: "ContractTransition",
                keyColumn: "Id",
                keyValue: 11,
                column: "Workflow",
                value: null);

            migrationBuilder.UpdateData(
                table: "ContractTransition",
                keyColumn: "Id",
                keyValue: 12,
                column: "Workflow",
                value: null);

            migrationBuilder.InsertData(
                table: "ContractTransition",
                columns: new[] { "Id", "Condition", "FromStatus", "IsActive", "NameAr", "NameEn", "RequiredActions", "RequiresNote", "SortOrder", "ToStatus", "Tone", "Workflow" },
                values: new object[] { 13, "complete_data", "draft", true, "اعتماد وتفعيل", "Approve & activate", "approve", false, 1, "active", "primary", "simple" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ContractTransition",
                keyColumn: "Id",
                keyValue: 13);

            migrationBuilder.DropColumn(
                name: "Workflow",
                table: "ContractTransition");
        }
    }
}
