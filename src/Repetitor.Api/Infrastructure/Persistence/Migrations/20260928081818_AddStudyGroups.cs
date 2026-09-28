using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repetitor.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStudyGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "study_groups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeacherUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_study_groups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_study_groups_users_TeacherUserId",
                        column: x => x.TeacherUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "study_group_courses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_study_group_courses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_study_group_courses_courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_study_group_courses_study_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "study_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "study_group_invitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    MaxUses = table.Column<int>(type: "integer", nullable: false),
                    UsedCount = table.Column<int>(type: "integer", nullable: false),
                    IsRevoked = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_study_group_invitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_study_group_invitations_study_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "study_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "study_group_members",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_study_group_members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_study_group_members_study_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "study_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_study_group_members_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_study_group_courses_CourseId",
                table: "study_group_courses",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_study_group_courses_GroupId_CourseId",
                table: "study_group_courses",
                columns: new[] { "GroupId", "CourseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_study_group_invitations_Code",
                table: "study_group_invitations",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_study_group_invitations_GroupId_IsRevoked",
                table: "study_group_invitations",
                columns: new[] { "GroupId", "IsRevoked" });

            migrationBuilder.CreateIndex(
                name: "IX_study_group_members_GroupId_UserId",
                table: "study_group_members",
                columns: new[] { "GroupId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_study_group_members_UserId",
                table: "study_group_members",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_study_groups_TeacherUserId_Name",
                table: "study_groups",
                columns: new[] { "TeacherUserId", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "study_group_courses");

            migrationBuilder.DropTable(
                name: "study_group_invitations");

            migrationBuilder.DropTable(
                name: "study_group_members");

            migrationBuilder.DropTable(
                name: "study_groups");
        }
    }
}
