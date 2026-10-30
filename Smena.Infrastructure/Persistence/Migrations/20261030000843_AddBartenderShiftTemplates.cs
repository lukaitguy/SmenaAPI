using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smena.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBartenderShiftTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Šanker ima iste smene kao konobar. Restorani koji su već postojali ih dobijaju ovde, a nove pravi
            // ShiftTemplate.Defaults. Šta već postoji se ne dira (menadžer je mogao sam da doda "I" ili "II").
            migrationBuilder.Sql("""
                INSERT INTO shift_templates (id, restaurant_id, position_id, name, start_time, end_time)
                SELECT gen_random_uuid(), p.restaurant_id, p.id, d.name, d.start_time::time, d.end_time::time
                FROM positions p
                JOIN (VALUES
                    ('Šanker', 'I',  '08:00', '16:00'),
                    ('Šanker', 'II', '16:00', '00:00')
                ) AS d(position_name, name, start_time, end_time) ON d.position_name = p.name
                WHERE NOT EXISTS (
                    SELECT 1 FROM shift_templates t WHERE t.position_id = p.id AND t.name = d.name);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM shift_templates t
                USING positions p
                WHERE t.position_id = p.id AND p.name = 'Šanker' AND t.name IN ('I', 'II');
                """);
        }
    }
}
