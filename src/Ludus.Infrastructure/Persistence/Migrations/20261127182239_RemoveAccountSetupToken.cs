using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ludus.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAccountSetupToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SetupTokenHash",
                table: "SecuritySettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SetupTokenHash",
                table: "SecuritySettings",
                type: "TEXT",
                nullable: true);
        }
    }
}
