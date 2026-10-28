using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smena.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenShiftsAndClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "employee_id",
                table: "shifts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            // xmin je Postgresova sistemska kolona i već postoji na svakoj tabeli, pa je ne dodajemo.

            migrationBuilder.CreateTable(
                name: "shift_claims",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    restaurant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shift_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    decided_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shift_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_shift_claims_employees_decided_by_employee_id",
                        column: x => x.decided_by_employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shift_claims_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shift_claims_restaurants_restaurant_id",
                        column: x => x.restaurant_id,
                        principalTable: "restaurants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_shift_claims_shifts_shift_id",
                        column: x => x.shift_id,
                        principalTable: "shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shift_claims_decided_by_employee_id",
                table: "shift_claims",
                column: "decided_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_shift_claims_employee_id",
                table: "shift_claims",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_shift_claims_restaurant_id_status",
                table: "shift_claims",
                columns: new[] { "restaurant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_shift_claims_shift_id",
                table: "shift_claims",
                column: "shift_id");

            migrationBuilder.CreateIndex(
                name: "ux_shift_claims_pending_per_employee",
                table: "shift_claims",
                columns: new[] { "shift_id", "employee_id" },
                unique: true,
                filter: "status = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shift_claims");

            migrationBuilder.AlterColumn<Guid>(
                name: "employee_id",
                table: "shifts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
