using System;
using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Service.Data.Migrations
{
    /// <summary>
    /// المرفقات المشتركة للوحدات الجديدة: سجل المرفق بمالكه (نوع + معرّف) ومزود تخزينه، ومحتواه لمزود قاعدة البيانات في جدول منفصل.
    /// جداول جديدة فقط؛ مرفقات المقاولات والإجازات القائمة لا تتغير.
    /// </summary>
    [DbContext(typeof(ErpDbContext))]
    [Migration("20261120210000_DocumentAttachments")]
    public partial class DocumentAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentAttachment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Checksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StorageProvider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UploadedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentAttachment", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentAttachmentContent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttachmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentAttachmentContent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentAttachmentContent_DocumentAttachment_AttachmentId",
                        column: x => x.AttachmentId,
                        principalTable: "DocumentAttachment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAttachment_TenantId",
                table: "DocumentAttachment",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAttachment_TenantId_OwnerType_OwnerId",
                table: "DocumentAttachment",
                columns: new[] { "TenantId", "OwnerType", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAttachmentContent_AttachmentId",
                table: "DocumentAttachmentContent",
                column: "AttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAttachmentContent_TenantId",
                table: "DocumentAttachmentContent",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAttachmentContent_TenantId_AttachmentId",
                table: "DocumentAttachmentContent",
                columns: new[] { "TenantId", "AttachmentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DocumentAttachmentContent");
            migrationBuilder.DropTable(name: "DocumentAttachment");
        }
    }
}
