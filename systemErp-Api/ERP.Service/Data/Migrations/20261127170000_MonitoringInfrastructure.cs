using System;
using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// بنية المراقبة: محاولات الإرسال للهيئة، تنبيهات المنشأة وسجلها وتشغيلات الفحص (جداول منشأة)، وسجل الدخول وأخطاء الخادم
    /// وفحوص المنصة (جداول منصة تُكتب في القاعدة المشتركة وحدها). سجل التدقيق: معرّف المستخدم وفهرسا الفترة والكيان
    /// (أعمدة البحث تُقصر قبل فهرستها، والقيم الأطول القديمة تُقص أولاً فلا يفشل التغيير).
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261127170000_MonitoringInfrastructure")]
    public partial class MonitoringInfrastructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---------- سجل التدقيق ----------
            migrationBuilder.Sql(@"
UPDATE [AuditLog] SET [Action] = LEFT([Action], 100) WHERE LEN([Action]) > 100;
UPDATE [AuditLog] SET [EntityName] = LEFT([EntityName], 128) WHERE LEN([EntityName]) > 128;
UPDATE [AuditLog] SET [EntityId] = LEFT([EntityId], 128) WHERE LEN([EntityId]) > 128;
UPDATE [AuditLog] SET [PerformedBy] = LEFT([PerformedBy], 256) WHERE LEN([PerformedBy]) > 256;
UPDATE [AuditLog] SET [ClientIpAddress] = LEFT([ClientIpAddress], 64) WHERE LEN([ClientIpAddress]) > 64;");

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "AuditLog",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Action", table: "AuditLog", type: "nvarchar(100)", maxLength: 100, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(max)");
            migrationBuilder.AlterColumn<string>(
                name: "EntityName", table: "AuditLog", type: "nvarchar(128)", maxLength: 128, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(max)");
            migrationBuilder.AlterColumn<string>(
                name: "EntityId", table: "AuditLog", type: "nvarchar(128)", maxLength: 128, nullable: true,
                oldClrType: typeof(string), oldType: "nvarchar(max)", oldNullable: true);
            migrationBuilder.AlterColumn<string>(
                name: "PerformedBy", table: "AuditLog", type: "nvarchar(256)", maxLength: 256, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(max)");
            migrationBuilder.AlterColumn<string>(
                name: "ClientIpAddress", table: "AuditLog", type: "nvarchar(64)", maxLength: 64, nullable: true,
                oldClrType: typeof(string), oldType: "nvarchar(max)", oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_TenantId_Timestamp",
                table: "AuditLog",
                columns: new[] { "TenantId", "Timestamp" });
            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_TenantId_EntityName_EntityId",
                table: "AuditLog",
                columns: new[] { "TenantId", "EntityName", "EntityId" });

            // ---------- جداول المنشأة ----------
            migrationBuilder.CreateTable(
                name: "ZatcaSubmissionAttempt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DocumentKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DocumentUuid = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Icv = table.Column<long>(type: "bigint", nullable: true),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    AttemptedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DurationMs = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    HttpStatus = table.Column<int>(type: "int", nullable: true),
                    IssueCodes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsResubmission = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_ZatcaSubmissionAttempt", x => x.Id));
            migrationBuilder.CreateIndex(name: "IX_ZatcaSubmissionAttempt_TenantId", table: "ZatcaSubmissionAttempt", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_ZatcaSubmissionAttempt_TenantId_AttemptedAt", table: "ZatcaSubmissionAttempt", columns: new[] { "TenantId", "AttemptedAt" });
            migrationBuilder.CreateIndex(name: "IX_ZatcaSubmissionAttempt_TenantId_InvoiceId", table: "ZatcaSubmissionAttempt", columns: new[] { "TenantId", "InvoiceId" });

            migrationBuilder.CreateTable(
                name: "MonitoringAlert",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Module = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ParamsJson = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CauseConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ConditionActive = table.Column<bool>(type: "bit", nullable: false),
                    FirstDetectedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastDetectedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OccurrenceCount = table.Column<int>(type: "int", nullable: false),
                    AssignedToUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignedToName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedByName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_MonitoringAlert", x => x.Id));
            migrationBuilder.CreateIndex(name: "IX_MonitoringAlert_TenantId", table: "MonitoringAlert", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_MonitoringAlert_TenantId_Fingerprint", table: "MonitoringAlert", columns: new[] { "TenantId", "Fingerprint" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_MonitoringAlert_TenantId_Status_LastDetectedAt", table: "MonitoringAlert", columns: new[] { "TenantId", "Status", "LastDetectedAt" });

            migrationBuilder.CreateTable(
                name: "MonitoringAlertEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AlertId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_MonitoringAlertEvent", x => x.Id));
            migrationBuilder.CreateIndex(name: "IX_MonitoringAlertEvent_TenantId", table: "MonitoringAlertEvent", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_MonitoringAlertEvent_TenantId_AlertId_At", table: "MonitoringAlertEvent", columns: new[] { "TenantId", "AlertId", "At" });

            migrationBuilder.CreateTable(
                name: "MonitoringCheckRun",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RunByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RunByName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SummaryJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AlertsOpened = table.Column<int>(type: "int", nullable: false),
                    AlertsCleared = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_MonitoringCheckRun", x => x.Id));
            migrationBuilder.CreateIndex(name: "IX_MonitoringCheckRun_TenantId", table: "MonitoringCheckRun", column: "TenantId");
            migrationBuilder.CreateIndex(name: "IX_MonitoringCheckRun_TenantId_StartedAt", table: "MonitoringCheckRun", columns: new[] { "TenantId", "StartedAt" });

            // ---------- جداول المنصة (القاعدة المشتركة) ----------
            migrationBuilder.CreateTable(
                name: "LoginEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Identifier = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_LoginEvent", x => x.Id));
            migrationBuilder.CreateIndex(name: "IX_LoginEvent_OccurredAt", table: "LoginEvent", column: "OccurredAt");
            migrationBuilder.CreateIndex(name: "IX_LoginEvent_TenantId_OccurredAt", table: "LoginEvent", columns: new[] { "TenantId", "OccurredAt" });

            migrationBuilder.CreateTable(
                name: "ApiErrorLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TraceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Path = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    ExceptionType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_ApiErrorLog", x => x.Id));
            migrationBuilder.CreateIndex(name: "IX_ApiErrorLog_OccurredAt", table: "ApiErrorLog", column: "OccurredAt");
            migrationBuilder.CreateIndex(name: "IX_ApiErrorLog_TenantId_OccurredAt", table: "ApiErrorLog", columns: new[] { "TenantId", "OccurredAt" });
            migrationBuilder.CreateIndex(name: "IX_ApiErrorLog_TraceId", table: "ApiErrorLog", column: "TraceId");

            migrationBuilder.CreateTable(
                name: "PlatformCheckRun",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RunByName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_PlatformCheckRun", x => x.Id));
            migrationBuilder.CreateIndex(name: "IX_PlatformCheckRun_StartedAt", table: "PlatformCheckRun", column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PlatformCheckRun");
            migrationBuilder.DropTable(name: "ApiErrorLog");
            migrationBuilder.DropTable(name: "LoginEvent");
            migrationBuilder.DropTable(name: "MonitoringCheckRun");
            migrationBuilder.DropTable(name: "MonitoringAlertEvent");
            migrationBuilder.DropTable(name: "MonitoringAlert");
            migrationBuilder.DropTable(name: "ZatcaSubmissionAttempt");

            migrationBuilder.DropIndex(name: "IX_AuditLog_TenantId_EntityName_EntityId", table: "AuditLog");
            migrationBuilder.DropIndex(name: "IX_AuditLog_TenantId_Timestamp", table: "AuditLog");
            migrationBuilder.DropColumn(name: "UserId", table: "AuditLog");

            migrationBuilder.AlterColumn<string>(name: "Action", table: "AuditLog", type: "nvarchar(max)", nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(100)", oldMaxLength: 100);
            migrationBuilder.AlterColumn<string>(name: "EntityName", table: "AuditLog", type: "nvarchar(max)", nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(128)", oldMaxLength: 128);
            migrationBuilder.AlterColumn<string>(name: "EntityId", table: "AuditLog", type: "nvarchar(max)", nullable: true,
                oldClrType: typeof(string), oldType: "nvarchar(128)", oldMaxLength: 128, oldNullable: true);
            migrationBuilder.AlterColumn<string>(name: "PerformedBy", table: "AuditLog", type: "nvarchar(max)", nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(256)", oldMaxLength: 256);
            migrationBuilder.AlterColumn<string>(name: "ClientIpAddress", table: "AuditLog", type: "nvarchar(max)", nullable: true,
                oldClrType: typeof(string), oldType: "nvarchar(64)", oldMaxLength: 64, oldNullable: true);
        }
    }
}
