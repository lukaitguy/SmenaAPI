using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smena.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftPublishing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "published_at",
                table: "shifts",
                type: "timestamp with time zone",
                nullable: true);

            // Smene koje su postojale pre objavljivanja važe kao objavljene, da zaposlenima ništa ne nestane.
            migrationBuilder.Sql("UPDATE shifts SET published_at = now() WHERE published_at IS NULL;");

            migrationBuilder.CreateIndex(
                name: "ix_shifts_employee_id_published_at",
                table: "shifts",
                columns: new[] { "employee_id", "published_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_shifts_employee_id_published_at",
                table: "shifts");

            migrationBuilder.DropColumn(
                name: "published_at",
                table: "shifts");
        }
    }
}
