using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhysioFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOptionalTwoFactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "auth_version",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "protected_two_factor_state",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "two_factor_prompt_handled",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Only accounts registered after this migration receive the onboarding suggestion.
            migrationBuilder.Sql("UPDATE \"AspNetUsers\" SET two_factor_prompt_handled = TRUE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auth_version",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "protected_two_factor_state",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "two_factor_prompt_handled",
                table: "AspNetUsers");
        }
    }
}
