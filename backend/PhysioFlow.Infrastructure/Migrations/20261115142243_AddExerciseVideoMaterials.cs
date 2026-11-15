using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhysioFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExerciseVideoMaterials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "video_id",
                table: "therapy_plan_exercises",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "video_id",
                table: "exercises",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "exercise_videos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    therapist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    youtube_video_id = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    content_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exercise_videos", x => x.id);
                    table.CheckConstraint("ck_exercise_videos_source", "(source = 'YouTube' AND youtube_video_id IS NOT NULL AND storage_key IS NULL AND content_type IS NULL AND size_bytes IS NULL) OR (source = 'Upload' AND youtube_video_id IS NULL AND storage_key IS NOT NULL AND content_type IS NOT NULL AND size_bytes IS NOT NULL AND size_bytes > 0 AND size_bytes <= 104857600)");
                    table.ForeignKey(
                        name: "fk_exercise_videos_therapists_therapist_id",
                        column: x => x.therapist_id,
                        principalTable: "therapists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_therapy_plan_exercises_video_id",
                table: "therapy_plan_exercises",
                column: "video_id");

            migrationBuilder.CreateIndex(
                name: "ix_exercises_video_id",
                table: "exercises",
                column: "video_id");

            migrationBuilder.CreateIndex(
                name: "ix_exercise_videos_therapist_id_id",
                table: "exercise_videos",
                columns: new[] { "therapist_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "fk_exercises_exercise_videos_video_id",
                table: "exercises",
                column: "video_id",
                principalTable: "exercise_videos",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_therapy_plan_exercises_exercise_videos_video_id",
                table: "therapy_plan_exercises",
                column: "video_id",
                principalTable: "exercise_videos",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Import recognized legacy library links. Existing plan positions and JSON history
            // retain their original content, including the absence of a captured video reference.
            migrationBuilder.Sql("""
                WITH recognized AS (
                    SELECT id, therapist_id, created_at, created_by_id,
                        COALESCE(
                            substring(btrim(youtube_url) FROM '(?i)^https?://(?:www\.|m\.|music\.)?youtube\.com/watch\?v=([a-z0-9_-]{11})(?:[&#].*)?$'),
                            substring(btrim(youtube_url) FROM '(?i)^https?://youtu\.be/([a-z0-9_-]{11})/?(?:[?#].*)?$'),
                            substring(btrim(youtube_url) FROM '(?i)^https?://(?:www\.|m\.|music\.)?youtube\.com/(?:embed|shorts|live)/([a-z0-9_-]{11})/?(?:[?#].*)?$'),
                            substring(btrim(youtube_url) FROM '(?i)^https?://(?:www\.)?youtube-nocookie\.com/embed/([a-z0-9_-]{11})/?(?:[?#].*)?$')
                        ) AS youtube_id
                    FROM exercises
                    WHERE youtube_url IS NOT NULL AND youtube_url !~ '[?&]v=[^#]*[?&]v='
                )
                INSERT INTO exercise_videos (id, therapist_id, source, youtube_video_id, created_at, created_by_id)
                SELECT id, therapist_id, 'YouTube', youtube_id, created_at, created_by_id
                FROM recognized WHERE youtube_id IS NOT NULL;

                UPDATE exercises AS e SET video_id = v.id, youtube_url = 'https://www.youtube.com/watch?v=' || v.youtube_video_id
                FROM exercise_videos AS v WHERE e.id = v.id;

                CREATE FUNCTION prevent_exercise_video_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Exercise video materials are immutable and retained for plan history.' USING ERRCODE = '23514';
                END;
                $$;
                CREATE TRIGGER exercise_videos_immutable BEFORE UPDATE OR DELETE ON exercise_videos
                FOR EACH ROW EXECUTE FUNCTION prevent_exercise_video_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS exercise_videos_immutable ON exercise_videos;
                DROP FUNCTION IF EXISTS prevent_exercise_video_mutation();
                """);
            migrationBuilder.DropForeignKey(
                name: "fk_exercises_exercise_videos_video_id",
                table: "exercises");

            migrationBuilder.DropForeignKey(
                name: "fk_therapy_plan_exercises_exercise_videos_video_id",
                table: "therapy_plan_exercises");

            migrationBuilder.DropTable(
                name: "exercise_videos");

            migrationBuilder.DropIndex(
                name: "ix_therapy_plan_exercises_video_id",
                table: "therapy_plan_exercises");

            migrationBuilder.DropIndex(
                name: "ix_exercises_video_id",
                table: "exercises");

            migrationBuilder.DropColumn(
                name: "video_id",
                table: "therapy_plan_exercises");

            migrationBuilder.DropColumn(
                name: "video_id",
                table: "exercises");
        }
    }
}
