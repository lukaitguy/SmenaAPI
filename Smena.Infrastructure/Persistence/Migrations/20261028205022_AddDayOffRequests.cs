using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smena.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDayOffRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "day_off_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    restaurant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    decided_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_day_off_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_day_off_requests_employees_decided_by_employee_id",
                        column: x => x.decided_by_employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_day_off_requests_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_day_off_requests_restaurants_restaurant_id",
                        column: x => x.restaurant_id,
                        principalTable: "restaurants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_day_off_requests_decided_by_employee_id",
                table: "day_off_requests",
                column: "decided_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_day_off_requests_restaurant_id_date",
                table: "day_off_requests",
                columns: new[] { "restaurant_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ux_day_off_requests_active_per_day",
                table: "day_off_requests",
                columns: new[] { "employee_id", "date" },
                unique: true,
                filter: "status IN ('Pending', 'Approved')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "day_off_requests");
        }
    }
}
