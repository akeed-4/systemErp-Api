using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Accounting.Persistence.Migrations
{
    /// <inheritdoc />
    internal partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "accounting");

            migrationBuilder.CreateTable(
                name: "Accounts",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Level = table.Column<int>(type: "int", nullable: false),
                    IsDebitNature = table.Column<bool>(type: "bit", nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    IsPostable = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CurrencyCode = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: true),
                    LinkedEntityType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    LinkedEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Accounts_Accounts_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "accounting",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CostCenters",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostCenters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FiscalPeriods",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsClosed = table.Column<bool>(type: "bit", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalPeriods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntries",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryNumber = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    SourceModule = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    SourceDocumentType = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceDocumentNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    PostingKind = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    TotalDebit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalCredit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ReversalOfId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReversedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PostedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PostedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PostingAccountMappings",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostingAccountMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PostingAccountMappings_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "accounting",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccountBalances",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FiscalPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Debit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Credit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountBalances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountBalances_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "accounting",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountBalances_FiscalPeriods_FiscalPeriodId",
                        column: x => x.FiscalPeriodId,
                        principalSchema: "accounting",
                        principalTable: "FiscalPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntryLines",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Debit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Credit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PartyType = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntryLines", x => x.Id);
                    table.CheckConstraint("CK_JournalEntryLines_Amounts", "[Debit] >= 0 AND [Credit] >= 0 AND ([Debit] = 0 OR [Credit] = 0) AND ([Debit] + [Credit]) > 0");
                    table.ForeignKey(
                        name: "FK_JournalEntryLines_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "accounting",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalEntryLines_CostCenters_CostCenterId",
                        column: x => x.CostCenterId,
                        principalSchema: "accounting",
                        principalTable: "CostCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalEntryLines_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalSchema: "accounting",
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountBalances_TenantId_AccountId_FiscalPeriodId",
                schema: "accounting",
                table: "AccountBalances",
                columns: new[] { "TenantId", "AccountId", "FiscalPeriodId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountBalances_TenantId_FiscalPeriodId",
                schema: "accounting",
                table: "AccountBalances",
                columns: new[] { "TenantId", "FiscalPeriodId" });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_TenantId_Code",
                schema: "accounting",
                table: "Accounts",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_TenantId_LinkedEntityType_LinkedEntityId",
                schema: "accounting",
                table: "Accounts",
                columns: new[] { "TenantId", "LinkedEntityType", "LinkedEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_TenantId_ParentId",
                schema: "accounting",
                table: "Accounts",
                columns: new[] { "TenantId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_CostCenters_TenantId_Code",
                schema: "accounting",
                table: "CostCenters",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FiscalPeriods_TenantId_Year_Month",
                schema: "accounting",
                table: "FiscalPeriods",
                columns: new[] { "TenantId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_TenantId_Date",
                schema: "accounting",
                table: "JournalEntries",
                columns: new[] { "TenantId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_TenantId_EntryNumber",
                schema: "accounting",
                table: "JournalEntries",
                columns: new[] { "TenantId", "EntryNumber" },
                unique: true,
                filter: "[EntryNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_JournalEntries_Source",
                schema: "accounting",
                table: "JournalEntries",
                columns: new[] { "TenantId", "SourceModule", "SourceDocumentType", "SourceDocumentId", "PostingKind" },
                unique: true,
                filter: "[ReversalOfId] IS NULL AND [Status] = 'posted'");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_TenantId_AccountId_JournalEntryId",
                schema: "accounting",
                table: "JournalEntryLines",
                columns: new[] { "TenantId", "AccountId", "JournalEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_TenantId_CostCenterId",
                schema: "accounting",
                table: "JournalEntryLines",
                columns: new[] { "TenantId", "CostCenterId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_TenantId_JournalEntryId",
                schema: "accounting",
                table: "JournalEntryLines",
                columns: new[] { "TenantId", "JournalEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_TenantId_PartyType_PartyId",
                schema: "accounting",
                table: "JournalEntryLines",
                columns: new[] { "TenantId", "PartyType", "PartyId" },
                filter: "[PartyId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PostingAccountMappings_TenantId_AccountId",
                schema: "accounting",
                table: "PostingAccountMappings",
                columns: new[] { "TenantId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_PostingAccountMappings_TenantId_Purpose",
                schema: "accounting",
                table: "PostingAccountMappings",
                columns: new[] { "TenantId", "Purpose" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountBalances",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "JournalEntryLines",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "PostingAccountMappings",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "FiscalPeriods",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "CostCenters",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "JournalEntries",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "Accounts",
                schema: "accounting");
        }
    }
}
