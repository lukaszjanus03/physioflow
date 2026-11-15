using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhysioFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientContentSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "published_at",
                table: "therapy_plans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "published_for_link_version",
                table: "therapy_plans",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "published_to_user_id",
                table: "therapy_plans",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "published_version_id",
                table: "therapy_plans",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "account_link_version",
                table: "patients",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "published_at",
                table: "medical_records",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "published_for_link_version",
                table: "medical_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "published_to_user_id",
                table: "medical_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "published_version_id",
                table: "medical_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_therapy_plans_published_to_user_id_published_at_id",
                table: "therapy_plans",
                columns: new[] { "published_to_user_id", "published_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_therapy_plans_published_version_id",
                table: "therapy_plans",
                column: "published_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_medical_records_published_to_user_id_published_at_id",
                table: "medical_records",
                columns: new[] { "published_to_user_id", "published_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_medical_records_published_version_id",
                table: "medical_records",
                column: "published_version_id");

            migrationBuilder.AddForeignKey(
                name: "fk_medical_records_medical_record_versions_published_version_id",
                table: "medical_records",
                column: "published_version_id",
                principalTable: "medical_record_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Also protect unlink/relink performed through SQL or maintenance tooling.
            // A grant belongs to one link incarnation, never merely to an account/card pair.
            migrationBuilder.Sql("""
                CREATE FUNCTION rotate_patient_account_link() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.user_id IS DISTINCT FROM OLD.user_id THEN
                        IF NEW.account_link_version = OLD.account_link_version THEN
                            NEW.account_link_version := gen_random_uuid();
                        END IF;
                        IF NEW.concurrency_stamp = OLD.concurrency_stamp THEN
                            NEW.concurrency_stamp := gen_random_uuid();
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER patient_account_link_changed BEFORE UPDATE OF user_id ON patients
                    FOR EACH ROW EXECUTE FUNCTION rotate_patient_account_link();
                """);

            migrationBuilder.AddForeignKey(
                name: "fk_therapy_plans_therapy_plan_versions_published_version_id",
                table: "therapy_plans",
                column: "published_version_id",
                principalTable: "therapy_plan_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS patient_account_link_changed ON patients;
                DROP FUNCTION IF EXISTS rotate_patient_account_link();
                """);
            migrationBuilder.DropForeignKey(
                name: "fk_medical_records_medical_record_versions_published_version_id",
                table: "medical_records");

            migrationBuilder.DropForeignKey(
                name: "fk_therapy_plans_therapy_plan_versions_published_version_id",
                table: "therapy_plans");

            migrationBuilder.DropIndex(
                name: "ix_therapy_plans_published_to_user_id_published_at_id",
                table: "therapy_plans");

            migrationBuilder.DropIndex(
                name: "ix_therapy_plans_published_version_id",
                table: "therapy_plans");

            migrationBuilder.DropIndex(
                name: "ix_medical_records_published_to_user_id_published_at_id",
                table: "medical_records");

            migrationBuilder.DropIndex(
                name: "ix_medical_records_published_version_id",
                table: "medical_records");

            migrationBuilder.DropColumn(
                name: "published_at",
                table: "therapy_plans");

            migrationBuilder.DropColumn(
                name: "published_for_link_version",
                table: "therapy_plans");

            migrationBuilder.DropColumn(
                name: "published_to_user_id",
                table: "therapy_plans");

            migrationBuilder.DropColumn(
                name: "published_version_id",
                table: "therapy_plans");

            migrationBuilder.DropColumn(
                name: "account_link_version",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "published_at",
                table: "medical_records");

            migrationBuilder.DropColumn(
                name: "published_for_link_version",
                table: "medical_records");

            migrationBuilder.DropColumn(
                name: "published_to_user_id",
                table: "medical_records");

            migrationBuilder.DropColumn(
                name: "published_version_id",
                table: "medical_records");
        }
    }
}
