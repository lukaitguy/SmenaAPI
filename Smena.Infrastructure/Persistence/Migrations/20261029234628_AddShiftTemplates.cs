using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smena.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "template_id",
                table: "shifts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "shift_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    restaurant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shift_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_shift_templates_positions_position_id",
                        column: x => x.position_id,
                        principalTable: "positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_shift_templates_restaurants_restaurant_id",
                        column: x => x.restaurant_id,
                        principalTable: "restaurants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shifts_template_id",
                table: "shifts",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_shift_templates_position_id",
                table: "shift_templates",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ix_shift_templates_restaurant_id_position_id_name",
                table: "shift_templates",
                columns: new[] { "restaurant_id", "position_id", "name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_shifts_shift_templates_template_id",
                table: "shifts",
                column: "template_id",
                principalTable: "shift_templates",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // Restorani koji su već postojali dobijaju iste podrazumevane smene kao novi (po nazivu pozicije).
            migrationBuilder.Sql("""
                INSERT INTO shift_templates (id, restaurant_id, position_id, name, start_time, end_time)
                SELECT gen_random_uuid(), p.restaurant_id, p.id, d.name, d.start_time::time, d.end_time::time
                FROM positions p
                JOIN (VALUES
                    ('Konobar', 'I',  '08:00', '16:00'),
                    ('Konobar', 'II', '16:00', '00:00'),
                    ('Kuvar',   'I',  '07:00', '15:00'),
                    ('Kuvar',   'II', '15:00', '23:00'),
                    ('Hostesa', 'II', '16:00', '00:00')
                ) AS d(position_name, name, start_time, end_time) ON d.position_name = p.name;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_shifts_shift_templates_template_id",
                table: "shifts");

            migrationBuilder.DropTable(
                name: "shift_templates");

            migrationBuilder.DropIndex(
                name: "ix_shifts_template_id",
                table: "shifts");

            migrationBuilder.DropColumn(
                name: "template_id",
                table: "shifts");
        }
    }
}
