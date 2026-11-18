using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class ApprovalEngineHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Status عمود مضمَّن في فهرس المستند: لا يتغير نوعه والفهرس قائم، فيُحذف ويُعاد بعد التعديل
            migrationBuilder.DropIndex(
                name: "IX_ApprovalRequest_TenantId_DocumentId",
                table: "ApprovalRequest");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "ApprovalRequest",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "RequiredApproverRole",
                table: "ApprovalRequest",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DueAt",
                table: "ApprovalRequest",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastActionAt",
                table: "ApprovalRequest",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "ApprovalRequest",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "SlaHours",
                table: "ApprovalPolicyStep",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Action",
                table: "ApprovalHistoryItem",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequest_TenantId_CreatedAt",
                table: "ApprovalRequest",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequest_TenantId_Status_RequiredApproverRole",
                table: "ApprovalRequest",
                columns: new[] { "TenantId", "Status", "RequiredApproverRole" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequest_TenantId_Status_RequiredApproverUserId",
                table: "ApprovalRequest",
                columns: new[] { "TenantId", "Status", "RequiredApproverUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequest_TenantId_DocumentId",
                table: "ApprovalRequest",
                columns: new[] { "TenantId", "DocumentId" })
                .Annotation("SqlServer:Include", new[] { "Status" });

            // السجل القائم قرارات فقط: حدثه = حالته (approved/rejected)
            migrationBuilder.Sql("UPDATE ApprovalHistoryItem SET [Action] = [Status] WHERE [Action] = N'';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApprovalRequest_TenantId_DocumentId",
                table: "ApprovalRequest");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalRequest_TenantId_CreatedAt",
                table: "ApprovalRequest");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalRequest_TenantId_Status_RequiredApproverRole",
                table: "ApprovalRequest");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalRequest_TenantId_Status_RequiredApproverUserId",
                table: "ApprovalRequest");

            migrationBuilder.DropColumn(
                name: "DueAt",
                table: "ApprovalRequest");

            migrationBuilder.DropColumn(
                name: "LastActionAt",
                table: "ApprovalRequest");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ApprovalRequest");

            migrationBuilder.DropColumn(
                name: "SlaHours",
                table: "ApprovalPolicyStep");

            migrationBuilder.DropColumn(
                name: "Action",
                table: "ApprovalHistoryItem");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "ApprovalRequest",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "RequiredApproverRole",
                table: "ApprovalRequest",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequest_TenantId_DocumentId",
                table: "ApprovalRequest",
                columns: new[] { "TenantId", "DocumentId" })
                .Annotation("SqlServer:Include", new[] { "Status" });
        }
    }
}
