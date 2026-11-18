using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace WorkTimeLog.Api.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false, collation: "NOCASE"),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TimeEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Minutes = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsCancelled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeEntries", x => x.Id);
                    table.CheckConstraint("CK_TimeEntries_Minutes", "\"Minutes\" > 0 AND \"Minutes\" <= 480");
                    table.ForeignKey(
                        name: "FK_TimeEntries_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Projects",
                columns: new[] { "Id", "IsActive", "Name" },
                values: new object[,]
                {
                    { 1, true, "โครงงานคอมพิวเตอร์" },
                    { 2, true, "ฝึกงาน / สหกิจศึกษา" },
                    { 3, true, "ทบทวนบทเรียนและการบ้าน" }
                });

            migrationBuilder.InsertData(
                table: "TimeEntries",
                columns: new[] { "Id", "CreatedAt", "Description", "IsCancelled", "Minutes", "Note", "ProjectId", "UpdatedAt", "WorkDate" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 11, 17, 9, 0, 0, 0, DateTimeKind.Utc), "ออกแบบฐานข้อมูลและ ER Diagram", false, 120, null, 1, new DateTime(2026, 11, 17, 9, 0, 0, 0, DateTimeKind.Utc), new DateOnly(2026, 11, 17) },
                    { 2, new DateTime(2026, 11, 17, 9, 0, 0, 0, DateTimeKind.Utc), "ประชุมทีมประจำสัปดาห์", false, 60, "ประชุมออนไลน์", 2, new DateTime(2026, 11, 17, 9, 0, 0, 0, DateTimeKind.Utc), new DateOnly(2026, 11, 17) },
                    { 3, new DateTime(2026, 11, 17, 9, 0, 0, 0, DateTimeKind.Utc), "ทำแบบฝึกหัดคณิตศาสตร์", true, 90, "ตัวอย่างรายการที่ถูกยกเลิก", 3, new DateTime(2026, 11, 17, 9, 0, 0, 0, DateTimeKind.Utc), new DateOnly(2026, 11, 17) },
                    { 4, new DateTime(2026, 11, 18, 9, 0, 0, 0, DateTimeKind.Utc), "เขียน Web API ด้วย ASP.NET Core", false, 180, null, 1, new DateTime(2026, 11, 18, 9, 0, 0, 0, DateTimeKind.Utc), new DateOnly(2026, 11, 18) },
                    { 5, new DateTime(2026, 11, 18, 9, 0, 0, 0, DateTimeKind.Utc), "จัดทำเอกสารรายงานประจำวัน", false, 90, "รออาจารย์ตรวจ", 2, new DateTime(2026, 11, 18, 9, 0, 0, 0, DateTimeKind.Utc), new DateOnly(2026, 11, 18) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Name",
                table: "Projects",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_ProjectId",
                table: "TimeEntries",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_WorkDate",
                table: "TimeEntries",
                column: "WorkDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TimeEntries");

            migrationBuilder.DropTable(
                name: "Projects");
        }
    }
}
