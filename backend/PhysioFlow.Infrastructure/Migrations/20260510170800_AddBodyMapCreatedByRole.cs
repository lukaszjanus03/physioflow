using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhysioFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBodyMapCreatedByRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_appointments_patients_patient_id",
                table: "appointments");

            migrationBuilder.DropForeignKey(
                name: "fk_appointments_therapists_therapist_id",
                table: "appointments");

            migrationBuilder.DropForeignKey(
                name: "fk_body_maps_appointments_appointment_id",
                table: "body_maps");

            migrationBuilder.DropForeignKey(
                name: "fk_medical_records_appointments_appointment_id",
                table: "medical_records");

            migrationBuilder.DropForeignKey(
                name: "fk_medical_records_patients_patient_id",
                table: "medical_records");

            migrationBuilder.DropForeignKey(
                name: "fk_medical_records_therapists_therapist_id",
                table: "medical_records");

            migrationBuilder.DropIndex(
                name: "ix_medical_records_patient_id",
                table: "medical_records");

            migrationBuilder.DropIndex(
                name: "ix_body_maps_patient_id",
                table: "body_maps");

            migrationBuilder.DropIndex(
                name: "ix_appointments_patient_id",
                table: "appointments");

            migrationBuilder.DropIndex(
                name: "ix_appointments_therapist_id",
                table: "appointments");

            migrationBuilder.AlterColumn<decimal>(
                name: "y",
                table: "pain_points",
                type: "numeric(6,5)",
                precision: 6,
                scale: 5,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<decimal>(
                name: "x",
                table: "pain_points",
                type: "numeric(6,5)",
                precision: 6,
                scale: 5,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<string>(
                name: "side",
                table: "pain_points",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "severity",
                table: "pain_points",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "note",
                table: "pain_points",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.Sql(@"
                ALTER TABLE medical_records
                ALTER COLUMN interview_form TYPE jsonb
                USING NULLIF(interview_form, '')::jsonb;
            ");

            migrationBuilder.AlterColumn<string>(
                name: "notes",
                table: "body_maps",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_by_role",
                table: "body_maps",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "type",
                table: "attachments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "storage_key",
                table: "attachments",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "file_name",
                table: "attachments",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "content_type",
                table: "attachments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "appointments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "reason",
                table: "appointments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_medical_records_patient_id_created_at",
                table: "medical_records",
                columns: new[] { "patient_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_body_maps_patient_id_recorded_at",
                table: "body_maps",
                columns: new[] { "patient_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_patient_id_starts_at",
                table: "appointments",
                columns: new[] { "patient_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_therapist_id_starts_at",
                table: "appointments",
                columns: new[] { "therapist_id", "starts_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_appointments_patients_patient_id",
                table: "appointments",
                column: "patient_id",
                principalTable: "patients",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_appointments_therapists_therapist_id",
                table: "appointments",
                column: "therapist_id",
                principalTable: "therapists",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_body_maps_appointments_appointment_id",
                table: "body_maps",
                column: "appointment_id",
                principalTable: "appointments",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_medical_records_appointments_appointment_id",
                table: "medical_records",
                column: "appointment_id",
                principalTable: "appointments",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_medical_records_patients_patient_id",
                table: "medical_records",
                column: "patient_id",
                principalTable: "patients",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_medical_records_therapists_therapist_id",
                table: "medical_records",
                column: "therapist_id",
                principalTable: "therapists",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_appointments_patients_patient_id",
                table: "appointments");

            migrationBuilder.DropForeignKey(
                name: "fk_appointments_therapists_therapist_id",
                table: "appointments");

            migrationBuilder.DropForeignKey(
                name: "fk_body_maps_appointments_appointment_id",
                table: "body_maps");

            migrationBuilder.DropForeignKey(
                name: "fk_medical_records_appointments_appointment_id",
                table: "medical_records");

            migrationBuilder.DropForeignKey(
                name: "fk_medical_records_patients_patient_id",
                table: "medical_records");

            migrationBuilder.DropForeignKey(
                name: "fk_medical_records_therapists_therapist_id",
                table: "medical_records");

            migrationBuilder.DropIndex(
                name: "ix_medical_records_patient_id_created_at",
                table: "medical_records");

            migrationBuilder.DropIndex(
                name: "ix_body_maps_patient_id_recorded_at",
                table: "body_maps");

            migrationBuilder.DropIndex(
                name: "ix_appointments_patient_id_starts_at",
                table: "appointments");

            migrationBuilder.DropIndex(
                name: "ix_appointments_therapist_id_starts_at",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "created_by_role",
                table: "body_maps");

            migrationBuilder.AlterColumn<decimal>(
                name: "y",
                table: "pain_points",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(6,5)",
                oldPrecision: 6,
                oldScale: 5);

            migrationBuilder.AlterColumn<decimal>(
                name: "x",
                table: "pain_points",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(6,5)",
                oldPrecision: 6,
                oldScale: 5);

            migrationBuilder.AlterColumn<int>(
                name: "side",
                table: "pain_points",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<int>(
                name: "severity",
                table: "pain_points",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "note",
                table: "pain_points",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "interview_form",
                table: "medical_records",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "notes",
                table: "body_maps",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "type",
                table: "attachments",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "storage_key",
                table: "attachments",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512);

            migrationBuilder.AlterColumn<string>(
                name: "file_name",
                table: "attachments",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "content_type",
                table: "attachments",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<int>(
                name: "status",
                table: "appointments",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "reason",
                table: "appointments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_medical_records_patient_id",
                table: "medical_records",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_body_maps_patient_id",
                table: "body_maps",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_patient_id",
                table: "appointments",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_therapist_id",
                table: "appointments",
                column: "therapist_id");

            migrationBuilder.AddForeignKey(
                name: "fk_appointments_patients_patient_id",
                table: "appointments",
                column: "patient_id",
                principalTable: "patients",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_appointments_therapists_therapist_id",
                table: "appointments",
                column: "therapist_id",
                principalTable: "therapists",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_body_maps_appointments_appointment_id",
                table: "body_maps",
                column: "appointment_id",
                principalTable: "appointments",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_medical_records_appointments_appointment_id",
                table: "medical_records",
                column: "appointment_id",
                principalTable: "appointments",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_medical_records_patients_patient_id",
                table: "medical_records",
                column: "patient_id",
                principalTable: "patients",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_medical_records_therapists_therapist_id",
                table: "medical_records",
                column: "therapist_id",
                principalTable: "therapists",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
