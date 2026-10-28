using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smena.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "series_id",
                table: "shifts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_shifts_series_id",
                table: "shifts",
                column: "series_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_shifts_series_id",
                table: "shifts");

            migrationBuilder.DropColumn(
                name: "series_id",
                table: "shifts");
        }
    }
}
