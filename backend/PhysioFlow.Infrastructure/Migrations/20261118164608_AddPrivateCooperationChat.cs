using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhysioFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPrivateCooperationChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cooperation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_link_version = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    therapist_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_sequence = table.Column<long>(type: "bigint", nullable: false),
                    patient_read_through = table.Column<long>(type: "bigint", nullable: false),
                    therapist_read_through = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_conversations", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_conversations_cooperation_requests_cooperation_id",
                        column: x => x.cooperation_id,
                        principalTable: "cooperation_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_chat_conversations_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "chat_send_quotas",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    window_started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_send_quotas", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "private_chat_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_private_chat_messages", x => x.id);
                    table.CheckConstraint("ck_private_chat_content", "length(btrim(content)) BETWEEN 1 AND 4000");
                    table.CheckConstraint("ck_private_chat_participants", "sender_id <> recipient_id");
                    table.CheckConstraint("ck_private_chat_sequence", "sequence > 0");
                    table.ForeignKey(
                        name: "fk_private_chat_messages_chat_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "chat_conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_conversations_context",
                table: "chat_conversations",
                columns: new[] { "cooperation_id", "account_link_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chat_conversations_patient_id",
                table: "chat_conversations",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_conversations_patient_user_id",
                table: "chat_conversations",
                column: "patient_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_conversations_therapist_user_id",
                table: "chat_conversations",
                column: "therapist_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_private_chat_messages_conversation_id_recipient_id_sequence",
                table: "private_chat_messages",
                columns: new[] { "conversation_id", "recipient_id", "sequence" },
                filter: "read_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_private_chat_messages_conversation_sequence",
                table: "private_chat_messages",
                columns: new[] { "conversation_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_private_chat_messages_sender_request",
                table: "private_chat_messages",
                columns: new[] { "sender_id", "request_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_send_quotas");

            migrationBuilder.DropTable(
                name: "private_chat_messages");

            migrationBuilder.DropTable(
                name: "chat_conversations");
        }
    }
}
