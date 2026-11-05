using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smena.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftWishes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shift_wishes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    restaurant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    preferred_shift = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shift_wishes", x => x.id);
                    table.ForeignKey(
                        name: "fk_shift_wishes_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shift_wishes_restaurants_restaurant_id",
                        column: x => x.restaurant_id,
                        principalTable: "restaurants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shift_wishes_employee_id_date",
                table: "shift_wishes",
                columns: new[] { "employee_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shift_wishes_restaurant_id_date",
                table: "shift_wishes",
                columns: new[] { "restaurant_id", "date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shift_wishes");
        }
    }
}
