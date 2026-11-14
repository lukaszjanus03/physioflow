using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhysioFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVisitDocumentationVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_medical_records_appointments_appointment_id",
                table: "medical_records");

            migrationBuilder.AddColumn<Guid>(
                name: "concurrency_stamp",
                table: "medical_records",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "current_version",
                table: "medical_records",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "functional_assessment",
                table: "medical_records",
                type: "character varying(10000)",
                maxLength: 10000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "interview",
                table: "medical_records",
                type: "character varying(10000)",
                maxLength: 10000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "patient_summary",
                table: "medical_records",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "performed_actions",
                table: "medical_records",
                type: "character varying(10000)",
                maxLength: 10000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recommendations",
                table: "medical_records",
                type: "character varying(10000)",
                maxLength: 10000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "medical_records",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Draft");

            // Existing records keep all legacy fields and receive usable edit tokens.
            migrationBuilder.Sql("UPDATE medical_records SET concurrency_stamp = gen_random_uuid();");

            migrationBuilder.CreateTable(
                name: "medical_record_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    medical_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    correction_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    interview = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    functional_assessment = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    performed_actions = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    recommendations = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    patient_summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_medical_record_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_medical_record_versions_medical_records_medical_record_id",
                        column: x => x.medical_record_id,
                        principalTable: "medical_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_medical_record_versions_record_version",
                table: "medical_record_versions",
                columns: new[] { "medical_record_id", "version_number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_medical_records_appointments_appointment_id",
                table: "medical_records",
                column: "appointment_id",
                principalTable: "appointments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_medical_records_appointments_appointment_id",
                table: "medical_records");

            migrationBuilder.DropTable(
                name: "medical_record_versions");

            migrationBuilder.DropColumn(
                name: "concurrency_stamp",
                table: "medical_records");

            migrationBuilder.DropColumn(
                name: "current_version",
                table: "medical_records");

            migrationBuilder.DropColumn(
                name: "functional_assessment",
                table: "medical_records");

            migrationBuilder.DropColumn(
                name: "interview",
                table: "medical_records");

            migrationBuilder.DropColumn(
                name: "patient_summary",
                table: "medical_records");

            migrationBuilder.DropColumn(
                name: "performed_actions",
                table: "medical_records");

            migrationBuilder.DropColumn(
                name: "recommendations",
                table: "medical_records");

            migrationBuilder.DropColumn(
                name: "status",
                table: "medical_records");

            migrationBuilder.AddForeignKey(
                name: "fk_medical_records_appointments_appointment_id",
                table: "medical_records",
                column: "appointment_id",
                principalTable: "appointments",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
