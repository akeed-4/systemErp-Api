using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ContractTransitionsByActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdvanceActions",
                table: "ContractStageDefinition");

            migrationBuilder.UpdateData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "active",
                column: "AllowedActions",
                value: "receive,post,inspect,approve");

            migrationBuilder.UpdateData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "approval",
                column: "AllowedActions",
                value: "approve");

            migrationBuilder.UpdateData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "draft",
                column: "AllowedActions",
                value: "edit,delete,submit");

            migrationBuilder.UpdateData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "initial_acceptance",
                column: "AllowedActions",
                value: "receive,post,approve");

            migrationBuilder.UpdateData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "review",
                column: "AllowedActions",
                value: "legal_review,financial_review");

            migrationBuilder.UpdateData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "suspended",
                column: "AllowedActions",
                value: "approve");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdvanceActions",
                table: "ContractStageDefinition",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "active",
                columns: new[] { "AdvanceActions", "AllowedActions" },
                values: new object[] { "close", "edit,post,close" });

            migrationBuilder.UpdateData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "approval",
                columns: new[] { "AdvanceActions", "AllowedActions" },
                values: new object[] { "approve,sign", "approve,sign,reject" });

            migrationBuilder.UpdateData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "cancelled",
                column: "AdvanceActions",
                value: "");

            migrationBuilder.UpdateData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "closed",
                column: "AdvanceActions",
                value: "");

            migrationBuilder.UpdateData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "draft",
                columns: new[] { "AdvanceActions", "AllowedActions" },
                values: new object[] { "edit", "edit,delete" });

            migrationBuilder.UpdateData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "initial_acceptance",
                columns: new[] { "AdvanceActions", "AllowedActions" },
                values: new object[] { "close", "edit,post,reject,close" });

            migrationBuilder.UpdateData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "review",
                columns: new[] { "AdvanceActions", "AllowedActions" },
                values: new object[] { "review", "review,reject" });

            migrationBuilder.UpdateData(
                table: "ContractStageDefinition",
                keyColumn: "Id",
                keyValue: "suspended",
                columns: new[] { "AdvanceActions", "AllowedActions" },
                values: new object[] { "", "" });
        }
    }
}
