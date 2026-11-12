using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhysioFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExpandTherapistMarketplaceProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "building_number",
                table: "therapists",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "education",
                table: "therapists",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "experience",
                table: "therapists",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "first_visit_preparation",
                table: "therapists",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "organizational_info",
                table: "therapists",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "photo_storage_key",
                table: "therapists",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "postal_code",
                table: "therapists",
                type: "character varying(6)",
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "public_email",
                table: "therapists",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "public_phone_number",
                table: "therapists",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "specializations",
                table: "therapists",
                type: "text[]",
                nullable: false,
                defaultValueSql: "ARRAY[]::text[]");

            migrationBuilder.AddColumn<string>(
                name: "street",
                table: "therapists",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "building_number",
                table: "therapists");

            migrationBuilder.DropColumn(
                name: "education",
                table: "therapists");

            migrationBuilder.DropColumn(
                name: "experience",
                table: "therapists");

            migrationBuilder.DropColumn(
                name: "first_visit_preparation",
                table: "therapists");

            migrationBuilder.DropColumn(
                name: "organizational_info",
                table: "therapists");

            migrationBuilder.DropColumn(
                name: "photo_storage_key",
                table: "therapists");

            migrationBuilder.DropColumn(
                name: "postal_code",
                table: "therapists");

            migrationBuilder.DropColumn(
                name: "public_email",
                table: "therapists");

            migrationBuilder.DropColumn(
                name: "public_phone_number",
                table: "therapists");

            migrationBuilder.DropColumn(
                name: "specializations",
                table: "therapists");

            migrationBuilder.DropColumn(
                name: "street",
                table: "therapists");
        }
    }
}
