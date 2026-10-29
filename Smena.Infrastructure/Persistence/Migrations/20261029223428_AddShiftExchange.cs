using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smena.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftExchange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "target_shift_id",
                table: "shift_swaps",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_shift_swaps_open_per_target_shift",
                table: "shift_swaps",
                column: "target_shift_id",
                unique: true,
                filter: "status IN ('Pending', 'Accepted') AND target_shift_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_shift_swaps_shifts_target_shift_id",
                table: "shift_swaps",
                column: "target_shift_id",
                principalTable: "shifts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_shift_swaps_shifts_target_shift_id",
                table: "shift_swaps");

            migrationBuilder.DropIndex(
                name: "ux_shift_swaps_open_per_target_shift",
                table: "shift_swaps");

            migrationBuilder.DropColumn(
                name: "target_shift_id",
                table: "shift_swaps");
        }
    }
}
