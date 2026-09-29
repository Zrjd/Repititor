using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repetitor.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLessonGenerationStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AiGenerationCompletedAt",
                table: "lessons",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiGenerationError",
                table: "lessons",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiGenerationRequest",
                table: "lessons",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AiGenerationRequestedAt",
                table: "lessons",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiGenerationStatus",
                table: "lessons",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.CreateIndex(
                name: "IX_lessons_AiGenerationStatus_AiGenerationRequestedAt",
                table: "lessons",
                columns: new[] { "AiGenerationStatus", "AiGenerationRequestedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_lessons_AiGenerationStatus_AiGenerationRequestedAt",
                table: "lessons");

            migrationBuilder.DropColumn(
                name: "AiGenerationCompletedAt",
                table: "lessons");

            migrationBuilder.DropColumn(
                name: "AiGenerationError",
                table: "lessons");

            migrationBuilder.DropColumn(
                name: "AiGenerationRequest",
                table: "lessons");

            migrationBuilder.DropColumn(
                name: "AiGenerationRequestedAt",
                table: "lessons");

            migrationBuilder.DropColumn(
                name: "AiGenerationStatus",
                table: "lessons");
        }
    }
}
