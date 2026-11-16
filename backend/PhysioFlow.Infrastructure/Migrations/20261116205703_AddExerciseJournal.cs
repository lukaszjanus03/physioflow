using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhysioFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExerciseJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exercise_journal_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    therapy_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    therapy_plan_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_version_number = table.Column<int>(type: "integer", nullable: false),
                    plan_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    publication_link_version = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    plan_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    exercise_snapshot_json = table.Column<string>(type: "text", nullable: false),
                    performed_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actual_sets = table.Column<int>(type: "integer", nullable: true),
                    actual_repetitions = table.Column<int>(type: "integer", nullable: true),
                    actual_duration_seconds = table.Column<int>(type: "integer", nullable: true),
                    pain_level = table.Column<int>(type: "integer", nullable: true),
                    comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exercise_journal_entries", x => x.id);
                    table.CheckConstraint("ck_exercise_journal_date", "performed_on >= DATE '1900-01-01'");
                    table.CheckConstraint("ck_exercise_journal_dose", "(actual_sets IS NULL OR actual_sets BETWEEN 1 AND 100) AND (actual_repetitions IS NULL OR actual_repetitions BETWEEN 1 AND 1000) AND (actual_duration_seconds IS NULL OR actual_duration_seconds BETWEEN 1 AND 86400) AND (actual_repetitions IS NULL OR actual_duration_seconds IS NULL) AND (status <> 'Skipped' OR (actual_sets IS NULL AND actual_repetitions IS NULL AND actual_duration_seconds IS NULL))");
                    table.CheckConstraint("ck_exercise_journal_pain", "pain_level IS NULL OR pain_level BETWEEN 0 AND 10");
                    table.CheckConstraint("ck_exercise_journal_status", "status IN ('Completed', 'Partial', 'Skipped')");
                    table.ForeignKey(
                        name: "fk_exercise_journal_entries_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_exercise_journal_entries_therapy_plan_versions_therapy_plan",
                        column: x => x.therapy_plan_version_id,
                        principalTable: "therapy_plan_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_exercise_journal_entries_account_request",
                table: "exercise_journal_entries",
                columns: new[] { "patient_user_id", "request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_exercise_journal_entries_patient_id",
                table: "exercise_journal_entries",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_exercise_journal_entries_patient_user_id_performed_on_creat",
                table: "exercise_journal_entries",
                columns: new[] { "patient_user_id", "performed_on", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_exercise_journal_entries_patient_user_id_therapy_plan_id_pe",
                table: "exercise_journal_entries",
                columns: new[] { "patient_user_id", "therapy_plan_id", "performed_on" });

            migrationBuilder.CreateIndex(
                name: "ix_exercise_journal_entries_therapy_plan_version_id",
                table: "exercise_journal_entries",
                column: "therapy_plan_version_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exercise_journal_entries");
        }
    }
}
