using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhysioFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTherapistReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "review_status_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    from_status = table.Column<int>(type: "integer", nullable: true),
                    to_status = table.Column<int>(type: "integer", nullable: true),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_status_changes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "therapist_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    therapist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_therapist_reviews", x => x.id);
                    table.CheckConstraint("ck_therapist_reviews_rating", "rating BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_therapist_reviews_status", "status IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "fk_therapist_reviews_asp_net_users_patient_user_id",
                        column: x => x.patient_user_id,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_therapist_reviews_therapists_therapist_id",
                        column: x => x.therapist_id,
                        principalTable: "therapists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_review_status_changes_review_id_occurred_at",
                table: "review_status_changes",
                columns: new[] { "review_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_therapist_reviews_status_created_at_id",
                table: "therapist_reviews",
                columns: new[] { "status", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_therapist_reviews_therapist_id_status_created_at_id",
                table: "therapist_reviews",
                columns: new[] { "therapist_id", "status", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_therapist_reviews_patient_therapist",
                table: "therapist_reviews",
                columns: new[] { "patient_user_id", "therapist_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "review_status_changes");

            migrationBuilder.DropTable(
                name: "therapist_reviews");
        }
    }
}
