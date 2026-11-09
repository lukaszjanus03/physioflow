using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhysioFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCooperationRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_patients_therapist_id",
                table: "patients");

            migrationBuilder.AddColumn<Guid>(
                name: "concurrency_stamp",
                table: "patients",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<bool>(
                name: "documentation_shared_with_account",
                table: "patients",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "cooperation_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    therapist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    patient_first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    patient_last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    patient_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    identity_verified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    card_link_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: true),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cooperation_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_cooperation_requests_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cooperation_requests_therapists_therapist_id",
                        column: x => x.therapist_id,
                        principalTable: "therapists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_patients_therapist_account",
                table: "patients",
                columns: new[] { "therapist_id", "user_id" },
                unique: true,
                filter: "user_id IS NOT NULL AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_cooperation_requests_open_pair",
                table: "cooperation_requests",
                columns: new[] { "patient_user_id", "therapist_id" },
                unique: true,
                filter: "status IN ('Pending', 'Accepted')");

            migrationBuilder.CreateIndex(
                name: "ix_cooperation_requests_patient_id",
                table: "cooperation_requests",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_cooperation_requests_patient_user_id_created_at",
                table: "cooperation_requests",
                columns: new[] { "patient_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_cooperation_requests_therapist_id_status",
                table: "cooperation_requests",
                columns: new[] { "therapist_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cooperation_requests");

            migrationBuilder.DropIndex(
                name: "ix_patients_therapist_account",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "concurrency_stamp",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "documentation_shared_with_account",
                table: "patients");

            migrationBuilder.CreateIndex(
                name: "ix_patients_therapist_id",
                table: "patients",
                column: "therapist_id");
        }
    }
}
