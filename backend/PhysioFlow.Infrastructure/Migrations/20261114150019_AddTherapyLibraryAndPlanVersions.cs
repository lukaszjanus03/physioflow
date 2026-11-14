using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhysioFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTherapyLibraryAndPlanVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_exercises_therapists_therapist_id",
                table: "exercises");

            migrationBuilder.DropForeignKey(
                name: "fk_therapy_plan_exercises_exercises_exercise_id",
                table: "therapy_plan_exercises");

            migrationBuilder.DropForeignKey(
                name: "fk_therapy_plan_exercises_therapy_plans_therapy_plan_id",
                table: "therapy_plan_exercises");

            migrationBuilder.DropForeignKey(
                name: "fk_therapy_plans_patients_patient_id",
                table: "therapy_plans");

            migrationBuilder.DropForeignKey(
                name: "fk_therapy_plans_therapists_therapist_id",
                table: "therapy_plans");

            migrationBuilder.DropIndex(
                name: "ix_therapy_plans_therapist_id",
                table: "therapy_plans");

            migrationBuilder.DropIndex(
                name: "ix_exercises_therapist_id",
                table: "exercises");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "start_date",
                table: "therapy_plans",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "therapy_plans",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<Guid>(
                name: "concurrency_stamp",
                table: "therapy_plans",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "current_version",
                table: "therapy_plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "general_recommendations",
                table: "therapy_plans",
                type: "character varying(10000)",
                maxLength: 10000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "goal",
                table: "therapy_plans",
                type: "character varying(10000)",
                maxLength: 10000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "therapy_plans",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Draft");

            migrationBuilder.AlterColumn<int>(
                name: "sets",
                table: "therapy_plan_exercises",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "repetitions",
                table: "therapy_plan_exercises",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "exercise_name",
                table: "therapy_plan_exercises",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "frequency",
                table: "therapy_plan_exercises",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "instructions",
                table: "therapy_plan_exercises",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "category",
                table: "exercises",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "concurrency_stamp",
                table: "exercises",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "instructions",
                table: "exercises",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_archived",
                table: "exercises",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                table: "exercises",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "therapy_plan_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    therapy_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    correction_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    content_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_therapy_plan_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_therapy_plan_versions_therapy_plans_therapy_plan_id",
                        column: x => x.therapy_plan_id,
                        principalTable: "therapy_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_therapy_plans_therapist_id_patient_id_created_at_id",
                table: "therapy_plans",
                columns: new[] { "therapist_id", "patient_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_exercises_therapist_id_is_archived_name_id",
                table: "exercises",
                columns: new[] { "therapist_id", "is_archived", "name", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_therapy_plan_versions_plan_version",
                table: "therapy_plan_versions",
                columns: new[] { "therapy_plan_id", "version_number" },
                unique: true);

            // Extend the existing tables without discarding legacy descriptions, media or parameters.
            migrationBuilder.Sql("""
                UPDATE exercises SET concurrency_stamp = gen_random_uuid(), instructions = description,
                    category = CASE muscle_group
                        WHEN 1 THEN 'Neck' WHEN 2 THEN 'Shoulders' WHEN 3 THEN 'Back'
                        WHEN 4 THEN 'Chest' WHEN 5 THEN 'Arms' WHEN 6 THEN 'Core'
                        WHEN 7 THEN 'Glutes' WHEN 8 THEN 'Legs' WHEN 9 THEN 'FullBody'
                        ELSE 'Other' END;
                UPDATE therapy_plans SET concurrency_stamp = gen_random_uuid();
                UPDATE therapy_plan_exercises AS item SET exercise_name = exercise.name,
                    instructions = exercise.description
                    FROM exercises AS exercise WHERE item.exercise_id = exercise.id;
                WITH ordered AS (
                    SELECT id, row_number() OVER (PARTITION BY therapy_plan_id ORDER BY "order", id) - 1 AS position
                    FROM therapy_plan_exercises
                ) UPDATE therapy_plan_exercises AS item SET "order" = ordered.position FROM ordered WHERE item.id = ordered.id;
                ALTER TABLE therapy_plan_exercises ADD CONSTRAINT uq_therapy_plan_exercises_order
                    UNIQUE (therapy_plan_id, "order") DEFERRABLE INITIALLY DEFERRED;
                """);

            // Protect activated snapshots even from raw SQL or synchronous EF updates/deletes.
            migrationBuilder.Sql("""
                CREATE FUNCTION prevent_therapy_plan_version_mutation() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    RAISE EXCEPTION 'Activated therapy plan versions are immutable.' USING ERRCODE = '23514';
                END;
                $body$;
                CREATE TRIGGER therapy_plan_version_immutable BEFORE UPDATE OR DELETE ON therapy_plan_versions
                    FOR EACH ROW EXECUTE FUNCTION prevent_therapy_plan_version_mutation();
                """);

            migrationBuilder.AddForeignKey(
                name: "fk_exercises_therapists_therapist_id",
                table: "exercises",
                column: "therapist_id",
                principalTable: "therapists",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_therapy_plan_exercises_exercises_exercise_id",
                table: "therapy_plan_exercises",
                column: "exercise_id",
                principalTable: "exercises",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_therapy_plan_exercises_therapy_plans_therapy_plan_id",
                table: "therapy_plan_exercises",
                column: "therapy_plan_id",
                principalTable: "therapy_plans",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_therapy_plans_patients_patient_id",
                table: "therapy_plans",
                column: "patient_id",
                principalTable: "patients",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_therapy_plans_therapists_therapist_id",
                table: "therapy_plans",
                column: "therapist_id",
                principalTable: "therapists",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER therapy_plan_version_immutable ON therapy_plan_versions;
                DROP FUNCTION prevent_therapy_plan_version_mutation();
                ALTER TABLE therapy_plan_exercises DROP CONSTRAINT uq_therapy_plan_exercises_order;
                """);
            migrationBuilder.DropForeignKey(
                name: "fk_exercises_therapists_therapist_id",
                table: "exercises");

            migrationBuilder.DropForeignKey(
                name: "fk_therapy_plan_exercises_exercises_exercise_id",
                table: "therapy_plan_exercises");

            migrationBuilder.DropForeignKey(
                name: "fk_therapy_plan_exercises_therapy_plans_therapy_plan_id",
                table: "therapy_plan_exercises");

            migrationBuilder.DropForeignKey(
                name: "fk_therapy_plans_patients_patient_id",
                table: "therapy_plans");

            migrationBuilder.DropForeignKey(
                name: "fk_therapy_plans_therapists_therapist_id",
                table: "therapy_plans");

            migrationBuilder.DropTable(
                name: "therapy_plan_versions");

            migrationBuilder.DropIndex(
                name: "ix_therapy_plans_therapist_id_patient_id_created_at_id",
                table: "therapy_plans");

            migrationBuilder.DropIndex(
                name: "ix_exercises_therapist_id_is_archived_name_id",
                table: "exercises");

            migrationBuilder.DropColumn(
                name: "concurrency_stamp",
                table: "therapy_plans");

            migrationBuilder.DropColumn(
                name: "current_version",
                table: "therapy_plans");

            migrationBuilder.DropColumn(
                name: "general_recommendations",
                table: "therapy_plans");

            migrationBuilder.DropColumn(
                name: "goal",
                table: "therapy_plans");

            migrationBuilder.DropColumn(
                name: "status",
                table: "therapy_plans");

            migrationBuilder.DropColumn(
                name: "exercise_name",
                table: "therapy_plan_exercises");

            migrationBuilder.DropColumn(
                name: "frequency",
                table: "therapy_plan_exercises");

            migrationBuilder.DropColumn(
                name: "instructions",
                table: "therapy_plan_exercises");

            migrationBuilder.DropColumn(
                name: "category",
                table: "exercises");

            migrationBuilder.DropColumn(
                name: "concurrency_stamp",
                table: "exercises");

            migrationBuilder.DropColumn(
                name: "instructions",
                table: "exercises");

            migrationBuilder.DropColumn(
                name: "is_archived",
                table: "exercises");

            migrationBuilder.DropColumn(
                name: "notes",
                table: "exercises");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "start_date",
                table: "therapy_plans",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "therapy_plans",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "sets",
                table: "therapy_plan_exercises",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "repetitions",
                table: "therapy_plan_exercises",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_therapy_plans_therapist_id",
                table: "therapy_plans",
                column: "therapist_id");

            migrationBuilder.CreateIndex(
                name: "ix_exercises_therapist_id",
                table: "exercises",
                column: "therapist_id");

            migrationBuilder.AddForeignKey(
                name: "fk_exercises_therapists_therapist_id",
                table: "exercises",
                column: "therapist_id",
                principalTable: "therapists",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_therapy_plan_exercises_exercises_exercise_id",
                table: "therapy_plan_exercises",
                column: "exercise_id",
                principalTable: "exercises",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_therapy_plan_exercises_therapy_plans_therapy_plan_id",
                table: "therapy_plan_exercises",
                column: "therapy_plan_id",
                principalTable: "therapy_plans",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_therapy_plans_patients_patient_id",
                table: "therapy_plans",
                column: "patient_id",
                principalTable: "patients",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_therapy_plans_therapists_therapist_id",
                table: "therapy_plans",
                column: "therapist_id",
                principalTable: "therapists",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
