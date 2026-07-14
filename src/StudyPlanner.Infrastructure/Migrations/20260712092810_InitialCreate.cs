using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace StudyPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudyItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Difficulty = table.Column<int>(type: "integer", nullable: false),
                    EstimatedDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    NextReviewDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudyItems_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudyItems_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudySessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlannedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AvailableMinutes = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TotalPlannedItems = table.Column<int>(type: "integer", nullable: false),
                    TotalPlannedMinutes = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudySessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudySessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProgressRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudyItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MinutesSpent = table.Column<int>(type: "integer", nullable: false),
                    ResultScore = table.Column<double>(type: "double precision", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgressRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProgressRecords_StudyItems_StudyItemId",
                        column: x => x.StudyItemId,
                        principalTable: "StudyItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProgressRecords_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudySessionItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudySessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudyItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlannedMinutes = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudySessionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudySessionItems_StudyItems_StudyItemId",
                        column: x => x.StudyItemId,
                        principalTable: "StudyItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudySessionItems_StudySessions_StudySessionId",
                        column: x => x.StudySessionId,
                        principalTable: "StudySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "Id", "Description", "IsDeleted", "Name" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-0000-0000-000000000001"), "Language learning", false, "Vocabulary" },
                    { new Guid("10000000-0000-0000-0000-000000000002"), "Software development", false, "Programming" },
                    { new Guid("10000000-0000-0000-0000-000000000003"), "Data structures & algorithms", false, "Algorithms" }
                });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "CreatedAt", "Email", "FullName", "IsActive", "PasswordHash", "UpdatedAt" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000001"), new DateTime(2026, 7, 12, 9, 28, 10, 442, DateTimeKind.Utc).AddTicks(1670), "demo@studyplanner.com", "Demo User", true, "ijB+qBaQa257yaJeoHCrzR/fBHVJ4IHm+mpkiCk7GY0=", null });

            migrationBuilder.InsertData(
                table: "StudyItems",
                columns: new[] { "Id", "CategoryId", "CreatedAt", "Description", "Difficulty", "EstimatedDurationMinutes", "IsCompleted", "IsDeleted", "NextReviewDate", "Priority", "Title", "Type", "UpdatedAt", "UserId" },
                values: new object[,]
                {
                    { new Guid("06fac01c-1184-4e0d-bce1-b068e0f8dd81"), new Guid("10000000-0000-0000-0000-000000000002"), new DateTime(2026, 7, 12, 9, 28, 10, 442, DateTimeKind.Utc).AddTicks(1750), "Study SOLID", 2, 60, false, false, new DateTime(2026, 7, 13, 9, 28, 10, 442, DateTimeKind.Utc).AddTicks(1750), 5, "Design Patterns", 2, null, new Guid("00000000-0000-0000-0000-000000000001") },
                    { new Guid("4ed4ecfa-0ad7-4fbc-a56d-8e7f4e4d87c5"), new Guid("10000000-0000-0000-0000-000000000003"), new DateTime(2026, 7, 12, 9, 28, 10, 442, DateTimeKind.Utc).AddTicks(1740), "Implement quicksort", 1, 45, false, false, new DateTime(2026, 7, 11, 9, 28, 10, 442, DateTimeKind.Utc).AddTicks(1740), 3, "Sorting Algorithms", 1, null, new Guid("00000000-0000-0000-0000-000000000001") },
                    { new Guid("e2224df1-5f86-438e-8e2b-0d9253495856"), new Guid("10000000-0000-0000-0000-000000000002"), new DateTime(2026, 7, 12, 9, 28, 10, 442, DateTimeKind.Utc).AddTicks(1730), "Intro to C#", 0, 30, false, false, new DateTime(2026, 7, 10, 9, 28, 10, 442, DateTimeKind.Utc).AddTicks(1700), 4, "Learn C# Basics", 2, null, new Guid("00000000-0000-0000-0000-000000000001") },
                    { new Guid("fa3d4c12-40fa-4936-922d-961311e628db"), new Guid("10000000-0000-0000-0000-000000000001"), new DateTime(2026, 7, 12, 9, 28, 10, 442, DateTimeKind.Utc).AddTicks(1750), "Basic greetings", 0, 20, false, false, new DateTime(2026, 7, 12, 9, 28, 10, 442, DateTimeKind.Utc).AddTicks(1740), 2, "Spanish Greetings", 0, null, new Guid("00000000-0000-0000-0000-000000000001") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Name",
                table: "Categories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProgressRecords_StudyItemId",
                table: "ProgressRecords",
                column: "StudyItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ProgressRecords_UserId",
                table: "ProgressRecords",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_StudyItems_CategoryId",
                table: "StudyItems",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_StudyItems_UserId",
                table: "StudyItems",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_StudySessionItems_StudyItemId",
                table: "StudySessionItems",
                column: "StudyItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StudySessionItems_StudySessionId",
                table: "StudySessionItems",
                column: "StudySessionId");

            migrationBuilder.CreateIndex(
                name: "IX_StudySessions_UserId",
                table: "StudySessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProgressRecords");

            migrationBuilder.DropTable(
                name: "StudySessionItems");

            migrationBuilder.DropTable(
                name: "StudyItems");

            migrationBuilder.DropTable(
                name: "StudySessions");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
