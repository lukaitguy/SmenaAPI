using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smena.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftSwaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shift_swaps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    restaurant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shift_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requester_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    decided_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shift_swaps", x => x.id);
                    table.ForeignKey(
                        name: "fk_shift_swaps_employees_decided_by_employee_id",
                        column: x => x.decided_by_employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shift_swaps_employees_requester_employee_id",
                        column: x => x.requester_employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shift_swaps_employees_target_employee_id",
                        column: x => x.target_employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shift_swaps_restaurants_restaurant_id",
                        column: x => x.restaurant_id,
                        principalTable: "restaurants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_shift_swaps_shifts_shift_id",
                        column: x => x.shift_id,
                        principalTable: "shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shift_swaps_decided_by_employee_id",
                table: "shift_swaps",
                column: "decided_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_shift_swaps_requester_employee_id",
                table: "shift_swaps",
                column: "requester_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_shift_swaps_restaurant_id_status",
                table: "shift_swaps",
                columns: new[] { "restaurant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_shift_swaps_target_employee_id",
                table: "shift_swaps",
                column: "target_employee_id");

            migrationBuilder.CreateIndex(
                name: "ux_shift_swaps_open_per_shift",
                table: "shift_swaps",
                column: "shift_id",
                unique: true,
                filter: "status IN ('Pending', 'Accepted')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shift_swaps");
        }
    }
}
