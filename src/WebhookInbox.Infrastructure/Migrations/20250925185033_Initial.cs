using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebhookInbox.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "endpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    Secret = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    RateLimitPerMinute = table.Column<int>(type: "integer", nullable: true),
                    PolicyJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_endpoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Headers = table.Column<string>(type: "jsonb", nullable: false),
                    Payload = table.Column<byte[]>(type: "bytea", nullable: false),
                    SignatureStatus = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "delivery_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EndpointId = table.Column<Guid>(type: "uuid", nullable: false),
                    Try = table.Column<int>(type: "integer", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResponseCode = table.Column<int>(type: "integer", nullable: true),
                    ResponseBody = table.Column<string>(type: "text", nullable: true),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_delivery_attempts_endpoints_EndpointId",
                        column: x => x.EndpointId,
                        principalTable: "endpoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_delivery_attempts_events_EventId",
                        column: x => x.EventId,
                        principalTable: "events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_delivery_attempts_EndpointId",
                table: "delivery_attempts",
                column: "EndpointId");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_attempts_EventId_EndpointId_Try",
                table: "delivery_attempts",
                columns: new[] { "EventId", "EndpointId", "Try" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_events_Source_ReceivedAt",
                table: "events",
                columns: new[] { "Source", "ReceivedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_attempts");

            migrationBuilder.DropTable(
                name: "endpoints");

            migrationBuilder.DropTable(
                name: "events");
        }
    }
}
