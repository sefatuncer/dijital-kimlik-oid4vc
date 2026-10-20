using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DijitalKimlik.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIssuer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.EnsureSchema(
                name: "issuer");

            migrationBuilder.CreateTable(
                name: "AuditLog",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TableName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Foreigners",
                schema: "issuer",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdentityNumber = table.Column<string>(type: "char(11)", unicode: false, fixedLength: true, maxLength: 11, nullable: false),
                    GivenName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FamilyName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BirthDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Nationality = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    PassportNumber = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Foreigners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StatusLists",
                schema: "issuer",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PublishedToken = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatusLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResidencePermits",
                schema: "issuer",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ForeignerId = table.Column<int>(type: "int", nullable: false),
                    PermitNumber = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Type = table.Column<byte>(type: "tinyint", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResidencePermits", x => x.Id);
                    table.CheckConstraint("CK_ResidencePermits_Period", "[ValidUntil] >= [ValidFrom]");
                    table.ForeignKey(
                        name: "FK_ResidencePermits_Foreigners_ForeignerId",
                        column: x => x.ForeignerId,
                        principalSchema: "issuer",
                        principalTable: "Foreigners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CredentialOffers",
                schema: "issuer",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResidencePermitId = table.Column<int>(type: "int", nullable: false),
                    PreAuthorizedCodeHash = table.Column<string>(type: "char(43)", unicode: false, fixedLength: true, maxLength: 43, nullable: false),
                    TxCodeHash = table.Column<string>(type: "char(43)", unicode: false, fixedLength: true, maxLength: 43, nullable: false),
                    FailedTxCodeAttempts = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RedeemedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CredentialOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CredentialOffers_ResidencePermits_ResidencePermitId",
                        column: x => x.ResidencePermitId,
                        principalSchema: "issuer",
                        principalTable: "ResidencePermits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IssuedCredentials",
                schema: "issuer",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CredentialId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ResidencePermitId = table.Column<int>(type: "int", nullable: false),
                    Format = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Vct = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    HolderKeyThumbprint = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    StatusListId = table.Column<int>(type: "int", nullable: false),
                    StatusListIndex = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    StatusReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StatusChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssuedCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssuedCredentials_ResidencePermits_ResidencePermitId",
                        column: x => x.ResidencePermitId,
                        principalSchema: "issuer",
                        principalTable: "ResidencePermits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IssuedCredentials_StatusLists_StatusListId",
                        column: x => x.StatusListId,
                        principalSchema: "issuer",
                        principalTable: "StatusLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_TableName_RecordKey",
                schema: "audit",
                table: "AuditLog",
                columns: new[] { "TableName", "RecordKey" });

            migrationBuilder.CreateIndex(
                name: "IX_CredentialOffers_ExpiresAt",
                schema: "issuer",
                table: "CredentialOffers",
                column: "ExpiresAt",
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CredentialOffers_PreAuthorizedCodeHash",
                schema: "issuer",
                table: "CredentialOffers",
                column: "PreAuthorizedCodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CredentialOffers_PublicId",
                schema: "issuer",
                table: "CredentialOffers",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CredentialOffers_ResidencePermitId",
                schema: "issuer",
                table: "CredentialOffers",
                column: "ResidencePermitId");

            migrationBuilder.CreateIndex(
                name: "IX_Foreigners_FamilyName_GivenName",
                schema: "issuer",
                table: "Foreigners",
                columns: new[] { "FamilyName", "GivenName" });

            migrationBuilder.CreateIndex(
                name: "IX_Foreigners_IdentityNumber",
                schema: "issuer",
                table: "Foreigners",
                column: "IdentityNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Foreigners_Nationality",
                schema: "issuer",
                table: "Foreigners",
                column: "Nationality");

            migrationBuilder.CreateIndex(
                name: "IX_IssuedCredentials_CredentialId",
                schema: "issuer",
                table: "IssuedCredentials",
                column: "CredentialId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IssuedCredentials_ExpiresAt",
                schema: "issuer",
                table: "IssuedCredentials",
                column: "ExpiresAt",
                filter: "[Status] IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "IX_IssuedCredentials_HolderKeyThumbprint",
                schema: "issuer",
                table: "IssuedCredentials",
                column: "HolderKeyThumbprint");

            migrationBuilder.CreateIndex(
                name: "IX_IssuedCredentials_ResidencePermitId",
                schema: "issuer",
                table: "IssuedCredentials",
                column: "ResidencePermitId");

            migrationBuilder.CreateIndex(
                name: "IX_IssuedCredentials_StatusListId_StatusListIndex",
                schema: "issuer",
                table: "IssuedCredentials",
                columns: new[] { "StatusListId", "StatusListIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResidencePermits_ForeignerId_Status",
                schema: "issuer",
                table: "ResidencePermits",
                columns: new[] { "ForeignerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ResidencePermits_PermitNumber",
                schema: "issuer",
                table: "ResidencePermits",
                column: "PermitNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLog",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "CredentialOffers",
                schema: "issuer");

            migrationBuilder.DropTable(
                name: "IssuedCredentials",
                schema: "issuer");

            migrationBuilder.DropTable(
                name: "ResidencePermits",
                schema: "issuer");

            migrationBuilder.DropTable(
                name: "StatusLists",
                schema: "issuer");

            migrationBuilder.DropTable(
                name: "Foreigners",
                schema: "issuer");
        }
    }
}
