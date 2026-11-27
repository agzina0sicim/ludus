using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ludus.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCalendarSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AppSettings_Calendar",
                table: "AppSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppSettings_CalendarHeaderFormat",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "CalendarHeaderFormat",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "FirstDayOfWeek",
                table: "AppSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CalendarHeaderFormat",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "ShortWeekdayDate");

            migrationBuilder.AddColumn<int>(
                name: "FirstDayOfWeek",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppSettings_Calendar",
                table: "AppSettings",
                sql: "\"FirstDayOfWeek\" BETWEEN 0 AND 6");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppSettings_CalendarHeaderFormat",
                table: "AppSettings",
                sql: "\"CalendarHeaderFormat\" IN ('ShortWeekday', 'ShortWeekdayDate', 'LongWeekday')");
        }
    }
}
