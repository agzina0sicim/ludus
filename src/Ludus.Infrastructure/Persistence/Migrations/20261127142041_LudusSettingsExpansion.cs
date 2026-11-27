using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ludus.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LudusSettingsExpansion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WebhookDeliveries_Event",
                table: "WebhookDeliveries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SecuritySettings_FormsCredentials",
                table: "SecuritySettings");

            migrationBuilder.DropIndex(
                name: "IX_QualityProfiles_Name",
                table: "QualityProfiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppSettings_Intervals",
                table: "AppSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppSettings_Language",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "FormsAuthenticationEnabled",
                table: "SecuritySettings");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Webhooks",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "Discord");

            migrationBuilder.AddColumn<string>(
                name: "Target",
                table: "Webhooks",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Token",
                table: "Webhooks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountSetupMode",
                table: "SecuritySettings",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "LegacyProtected");

            migrationBuilder.AddColumn<string>(
                name: "AuthenticationMethod",
                table: "SecuritySettings",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "Forms");

            migrationBuilder.AddColumn<string>(
                name: "SetupTokenHash",
                table: "SecuritySettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PlatformId",
                table: "QualityProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "ManagedFolderRelativePath",
                table: "Games",
                type: "TEXT",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReleaseDatePrecision",
                table: "Games",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReleaseDateUtc",
                table: "Games",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FailureHandledAtUtc",
                table: "Downloads",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "Downloads",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "Automatic");

            migrationBuilder.AddColumn<string>(
                name: "CalendarHeaderFormat",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "ShortWeekdayDate");

            migrationBuilder.AddColumn<bool>(
                name: "CreateEmptyFolders",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DeleteEmptyFolders",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DirectoryPermissions",
                table: "AppSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "0775");

            migrationBuilder.AddColumn<bool>(
                name: "EnableCompletedDownloadHandling",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "FilePermissions",
                table: "AppSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "0664");

            migrationBuilder.AddColumn<int>(
                name: "FirstDayOfWeek",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "LongDateFormat",
                table: "AppSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "dddd, d. MMMM yyyy");

            migrationBuilder.AddColumn<long>(
                name: "MaximumIndexerSizeBytes",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "MediaGroup",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetadataLanguage",
                table: "AppSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "de");

            migrationBuilder.AddColumn<int>(
                name: "MinimumAgeMinutes",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "MinimumFreeSpaceBytes",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1073741824L);

            migrationBuilder.AddColumn<bool>(
                name: "RedownloadFailed",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "RedownloadFailedFromInteractiveSearch",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ShortDateFormat",
                table: "AppSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "dd.MM.yyyy");

            migrationBuilder.AddColumn<bool>(
                name: "SkipFreeSpaceCheck",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Theme",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "Auto");

            migrationBuilder.AddColumn<bool>(
                name: "Use12HourTime",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE "SecuritySettings"
                SET "AccountSetupMode" = CASE
                    WHEN "Username" IS NOT NULL AND length(trim("Username")) > 0
                         AND "PasswordHash" IS NOT NULL AND length("PasswordHash") > 0 THEN 'Complete'
                    ELSE 'LegacyProtected'
                END,
                "AuthenticationMethod" = 'Forms';

                UPDATE "AppSettings" SET "MetadataLanguage" = "Language";
                UPDATE "Games"
                SET "ReleaseDateUtc" = printf('%04d-01-01T00:00:00.0000000Z', "ReleaseYear"),
                    "ReleaseDatePrecision" = 'Year'
                WHERE "ReleaseYear" IS NOT NULL AND "ReleaseDateUtc" IS NULL;

                CREATE TEMP TABLE "__LudusProfileSources" AS
                    SELECT "Id" AS "ProfileId", "Name" FROM "QualityProfiles";

                UPDATE "QualityProfiles"
                SET "PlatformId" = COALESCE(
                    (SELECT MIN("Games"."PlatformId") FROM "Games" WHERE "Games"."QualityProfileId" = "QualityProfiles"."Id"),
                    (SELECT MIN("Platforms"."Id") FROM "Platforms"));

                INSERT INTO "QualityProfiles" (
                    "Name", "PlatformId", "MinimumScore", "AllowUpgrades", "UpgradeUntilScore",
                    "AllowedRegions", "ExcludedRegions", "RejectBeta", "RejectDemo", "RejectPrototype",
                    "AllowUpdates", "AllowDlc", "CreatedAtUtc", "UpdatedAtUtc", "Version")
                SELECT source."Name", assignment."PlatformId", profile."MinimumScore", profile."AllowUpgrades",
                    profile."UpgradeUntilScore", profile."AllowedRegions", profile."ExcludedRegions",
                    profile."RejectBeta", profile."RejectDemo", profile."RejectPrototype", profile."AllowUpdates",
                    profile."AllowDlc", profile."CreatedAtUtc", profile."UpdatedAtUtc", profile."Version"
                FROM "__LudusProfileSources" source
                JOIN "QualityProfiles" profile ON profile."Id" = source."ProfileId"
                JOIN (SELECT DISTINCT "QualityProfileId", "PlatformId" FROM "Games") assignment
                    ON assignment."QualityProfileId" = source."ProfileId"
                WHERE assignment."PlatformId" <> profile."PlatformId";

                INSERT INTO "QualityProfileFormats" ("QualityProfileId", "CustomFormatId", "Score")
                SELECT clone."Id", format."CustomFormatId", format."Score"
                FROM "QualityProfiles" clone
                JOIN "__LudusProfileSources" source ON source."Name" = clone."Name"
                JOIN "QualityProfileFormats" format ON format."QualityProfileId" = source."ProfileId"
                WHERE clone."Id" <> source."ProfileId";

                UPDATE "Games"
                SET "QualityProfileId" = (
                    SELECT destination."Id"
                    FROM "__LudusProfileSources" source
                    JOIN "QualityProfiles" destination ON destination."Name" = source."Name"
                    WHERE source."ProfileId" = "Games"."QualityProfileId"
                      AND destination."PlatformId" = "Games"."PlatformId"
                    LIMIT 1)
                WHERE EXISTS (
                    SELECT 1 FROM "__LudusProfileSources" source
                    JOIN "QualityProfiles" destination ON destination."Name" = source."Name"
                    WHERE source."ProfileId" = "Games"."QualityProfileId"
                      AND destination."PlatformId" = "Games"."PlatformId");

                DROP TABLE "__LudusProfileSources";
                """);

            migrationBuilder.CreateTable(
                name: "GameMetadataTranslations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<long>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    TitleLanguage = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    DescriptionLanguage = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameMetadataTranslations", x => x.Id);
                    table.CheckConstraint("CK_GameMetadataTranslations_Provider", "\"Provider\" IN ('Igdb', 'ScreenScraper')");
                    table.ForeignKey(
                        name: "FK_GameMetadataTranslations_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Webhooks_Kind",
                table: "Webhooks",
                sql: "\"Kind\" IN ('Discord', 'Gotify', 'Telegram', 'Generic')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WebhookDeliveries_Event",
                table: "WebhookDeliveries",
                sql: "\"Event\" IN ('Grabbed', 'Imported', 'ImportFailed', 'Upgraded', 'DownloadFailed')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecuritySettings_AccountCredentials",
                table: "SecuritySettings",
                sql: "\"AccountSetupMode\" <> 'Complete' OR (\"Username\" IS NOT NULL AND length(trim(\"Username\")) > 0 AND \"PasswordHash\" IS NOT NULL AND length(\"PasswordHash\") > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecuritySettings_AccountSetupMode",
                table: "SecuritySettings",
                sql: "\"AccountSetupMode\" IN ('NewInstance', 'LegacyProtected', 'Complete')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecuritySettings_AuthenticationMethod",
                table: "SecuritySettings",
                sql: "\"AuthenticationMethod\" IN ('Forms', 'Basic')");

            migrationBuilder.CreateIndex(
                name: "IX_QualityProfiles_PlatformId_Name",
                table: "QualityProfiles",
                columns: new[] { "PlatformId", "Name" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Games_ReleaseDatePrecision",
                table: "Games",
                sql: "\"ReleaseDatePrecision\" IN ('Exact', 'Month', 'Year')");

            migrationBuilder.CreateIndex(
                name: "IX_Downloads_Status_FailureHandledAtUtc",
                table: "Downloads",
                columns: new[] { "Status", "FailureHandledAtUtc" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Downloads_Origin",
                table: "Downloads",
                sql: "\"Origin\" IN ('Automatic', 'Interactive')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppSettings_Calendar",
                table: "AppSettings",
                sql: "\"FirstDayOfWeek\" BETWEEN 0 AND 6");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppSettings_CalendarHeaderFormat",
                table: "AppSettings",
                sql: "\"CalendarHeaderFormat\" IN ('ShortWeekday', 'ShortWeekdayDate', 'LongWeekday')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppSettings_Intervals",
                table: "AppSettings",
                sql: "\"RssIntervalMinutes\" >= 1 AND \"MinimumAgeMinutes\" >= 0 AND \"DownloadRefreshIntervalSeconds\" >= 5 AND \"LibraryScanIntervalHours\" >= 1 AND \"HealthCheckIntervalMinutes\" >= 1 AND \"BackupIntervalHours\" >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppSettings_Language",
                table: "AppSettings",
                sql: "\"Language\" IN ('de', 'en') AND length(trim(\"MetadataLanguage\")) BETWEEN 2 AND 16");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppSettings_Permissions",
                table: "AppSettings",
                sql: "\"FilePermissions\" GLOB '0[0-7][0-7][0-7]' AND \"DirectoryPermissions\" GLOB '0[0-7][0-7][0-7]'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppSettings_Storage",
                table: "AppSettings",
                sql: "\"MinimumFreeSpaceBytes\" >= 0 AND \"MaximumIndexerSizeBytes\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppSettings_Theme",
                table: "AppSettings",
                sql: "\"Theme\" IN ('Light', 'Dark', 'Auto')");

            migrationBuilder.CreateIndex(
                name: "IX_GameMetadataTranslations_GameId_Provider_Language",
                table: "GameMetadataTranslations",
                columns: new[] { "GameId", "Provider", "Language" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_QualityProfiles_Platforms_PlatformId",
                table: "QualityProfiles",
                column: "PlatformId",
                principalTable: "Platforms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QualityProfiles_Platforms_PlatformId",
                table: "QualityProfiles");

            migrationBuilder.DropTable(
                name: "GameMetadataTranslations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Webhooks_Kind",
                table: "Webhooks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WebhookDeliveries_Event",
                table: "WebhookDeliveries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SecuritySettings_AccountCredentials",
                table: "SecuritySettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SecuritySettings_AccountSetupMode",
                table: "SecuritySettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SecuritySettings_AuthenticationMethod",
                table: "SecuritySettings");

            migrationBuilder.DropIndex(
                name: "IX_QualityProfiles_PlatformId_Name",
                table: "QualityProfiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Games_ReleaseDatePrecision",
                table: "Games");

            migrationBuilder.DropIndex(
                name: "IX_Downloads_Status_FailureHandledAtUtc",
                table: "Downloads");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Downloads_Origin",
                table: "Downloads");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppSettings_Calendar",
                table: "AppSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppSettings_CalendarHeaderFormat",
                table: "AppSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppSettings_Intervals",
                table: "AppSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppSettings_Language",
                table: "AppSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppSettings_Permissions",
                table: "AppSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppSettings_Storage",
                table: "AppSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppSettings_Theme",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Webhooks");

            migrationBuilder.DropColumn(
                name: "Target",
                table: "Webhooks");

            migrationBuilder.DropColumn(
                name: "Token",
                table: "Webhooks");

            migrationBuilder.DropColumn(
                name: "AccountSetupMode",
                table: "SecuritySettings");

            migrationBuilder.DropColumn(
                name: "AuthenticationMethod",
                table: "SecuritySettings");

            migrationBuilder.DropColumn(
                name: "SetupTokenHash",
                table: "SecuritySettings");

            migrationBuilder.DropColumn(
                name: "PlatformId",
                table: "QualityProfiles");

            migrationBuilder.DropColumn(
                name: "ManagedFolderRelativePath",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "ReleaseDatePrecision",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "ReleaseDateUtc",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "FailureHandledAtUtc",
                table: "Downloads");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "Downloads");

            migrationBuilder.DropColumn(
                name: "CalendarHeaderFormat",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "CreateEmptyFolders",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "DeleteEmptyFolders",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "DirectoryPermissions",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "EnableCompletedDownloadHandling",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "FilePermissions",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "FirstDayOfWeek",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "LongDateFormat",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "MaximumIndexerSizeBytes",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "MediaGroup",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "MetadataLanguage",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "MinimumAgeMinutes",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "MinimumFreeSpaceBytes",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "RedownloadFailed",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "RedownloadFailedFromInteractiveSearch",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "ShortDateFormat",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "SkipFreeSpaceCheck",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "Theme",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "Use12HourTime",
                table: "AppSettings");

            migrationBuilder.AddColumn<bool>(
                name: "FormsAuthenticationEnabled",
                table: "SecuritySettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_WebhookDeliveries_Event",
                table: "WebhookDeliveries",
                sql: "\"Event\" IN ('Grabbed', 'Imported', 'Upgraded', 'DownloadFailed')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SecuritySettings_FormsCredentials",
                table: "SecuritySettings",
                sql: "\"FormsAuthenticationEnabled\" = 0 OR (\"Username\" IS NOT NULL AND length(trim(\"Username\")) > 0 AND \"PasswordHash\" IS NOT NULL AND length(\"PasswordHash\") > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_QualityProfiles_Name",
                table: "QualityProfiles",
                column: "Name",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppSettings_Intervals",
                table: "AppSettings",
                sql: "\"RssIntervalMinutes\" >= 1 AND \"DownloadRefreshIntervalSeconds\" >= 5 AND \"LibraryScanIntervalHours\" >= 1 AND \"HealthCheckIntervalMinutes\" >= 1 AND \"BackupIntervalHours\" >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppSettings_Language",
                table: "AppSettings",
                sql: "\"Language\" IN ('de', 'en')");
        }
    }
}
