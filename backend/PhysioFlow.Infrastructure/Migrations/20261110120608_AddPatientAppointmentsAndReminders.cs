using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhysioFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientAppointmentsAndReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "patient_user_id",
                table: "appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reminder_schedule_id",
                table: "appointments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql("""
                UPDATE appointments SET reminder_schedule_id = gen_random_uuid();

                -- Historyczne powiązanie obowiązujące w chwili utworzenia wizyty.
                -- Pierwsze powiązanie obejmuje także wcześniejsze wizyty z tej karty.
                WITH owners AS (
                    SELECT a.id, (array_agg(DISTINCT r.patient_user_id))[1] AS user_id
                    FROM appointments a
                    JOIN cooperation_requests r ON r.patient_id = a.patient_id
                    WHERE r.status IN ('Accepted', 'Ended')
                      AND r.decided_at IS NOT NULL
                      AND (r.ended_at IS NULL OR a.created_at <= r.ended_at)
                      AND (a.created_at >= r.decided_at OR NOT EXISTS (
                          SELECT 1 FROM cooperation_requests older
                          WHERE older.patient_id = r.patient_id
                            AND older.status IN ('Accepted', 'Ended')
                            AND older.decided_at < r.decided_at))
                    GROUP BY a.id
                    HAVING count(DISTINCT r.patient_user_id) = 1
                )
                UPDATE appointments a SET patient_user_id = owners.user_id
                FROM owners WHERE a.id = owners.id;

                -- Starsze bezpośrednie powiązania, bez sprzecznej historii współprac.
                UPDATE appointments a SET patient_user_id = p.user_id
                FROM patients p
                WHERE a.patient_id = p.id AND a.patient_user_id IS NULL AND p.user_id IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM cooperation_requests r
                      WHERE r.patient_id = p.id AND r.status IN ('Accepted', 'Ended')
                        AND r.patient_user_id <> p.user_id);
                """);

            migrationBuilder.CreateTable(
                name: "appointment_reminders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    attempt_window_started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    recipient = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    html_body = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointment_reminders", x => x.id);
                    table.ForeignKey(
                        name: "fk_appointment_reminders_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_patient_user_id_starts_at",
                table: "appointments",
                columns: new[] { "patient_user_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_appointment_reminders_appointment_id_schedule_id",
                table: "appointment_reminders",
                columns: new[] { "appointment_id", "schedule_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "appointment_reminders");

            migrationBuilder.DropIndex(
                name: "ix_appointments_patient_user_id_starts_at",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "patient_user_id",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "reminder_schedule_id",
                table: "appointments");
        }
    }
}
