using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhysioFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFavoriteTherapists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "favorite_therapists",
                columns: table => new
                {
                    patient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    therapist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_favorite_therapists", x => new { x.patient_user_id, x.therapist_id });
                    table.ForeignKey(
                        name: "fk_favorite_therapists_asp_net_users_patient_user_id",
                        column: x => x.patient_user_id,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_favorite_therapists_therapists_therapist_id",
                        column: x => x.therapist_id,
                        principalTable: "therapists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_favorite_therapists_patient_user_id_created_at_therapist_id",
                table: "favorite_therapists",
                columns: new[] { "patient_user_id", "created_at", "therapist_id" });

            migrationBuilder.CreateIndex(
                name: "ix_favorite_therapists_therapist_id",
                table: "favorite_therapists",
                column: "therapist_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "favorite_therapists");
        }
    }
}
