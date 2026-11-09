using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhysioFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTherapistCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "notes",
                table: "appointments",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "buffer_minutes",
                table: "appointments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "concurrency_stamp",
                table: "appointments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "therapist_calendar_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    therapist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    buffer_minutes = table.Column<int>(type: "integer", nullable: false),
                    allowed_durations = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_therapist_calendar_settings", x => x.id);
                    table.ForeignKey(
                        name: "fk_therapist_calendar_settings_therapists_therapist_id",
                        column: x => x.therapist_id,
                        principalTable: "therapists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_availability_slots_therapist_id_day_of_week",
                table: "availability_slots",
                columns: new[] { "therapist_id", "day_of_week" });

            migrationBuilder.CreateIndex(
                name: "ix_availability_slots_therapist_id_specific_date",
                table: "availability_slots",
                columns: new[] { "therapist_id", "specific_date" });

            migrationBuilder.CreateIndex(
                name: "ix_therapist_calendar_settings_therapist_id",
                table: "therapist_calendar_settings",
                column: "therapist_id",
                unique: true);

            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS btree_gist;
                UPDATE appointments
                SET concurrency_stamp = gen_random_uuid()
                WHERE concurrency_stamp = '00000000-0000-0000-0000-000000000000';
                ALTER TABLE appointments
                ADD CONSTRAINT ex_appointments_therapist_schedule
                EXCLUDE USING gist (
                    therapist_id WITH =,
                    tstzrange(starts_at, ends_at, '[)') WITH &&
                )
                WHERE (status = 'Scheduled' AND is_deleted = false);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE appointments DROP CONSTRAINT IF EXISTS ex_appointments_therapist_schedule;
                """);

            migrationBuilder.DropTable(
                name: "therapist_calendar_settings");

            migrationBuilder.DropIndex(
                name: "ix_availability_slots_therapist_id_day_of_week",
                table: "availability_slots");

            migrationBuilder.DropIndex(
                name: "ix_availability_slots_therapist_id_specific_date",
                table: "availability_slots");

            migrationBuilder.DropColumn(
                name: "buffer_minutes",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "concurrency_stamp",
                table: "appointments");

            migrationBuilder.AlterColumn<string>(
                name: "notes",
                table: "appointments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);
        }
    }
}
