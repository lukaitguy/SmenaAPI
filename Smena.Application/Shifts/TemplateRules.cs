using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Application.DayOffs;
using Smena.Domain.Shifts;

namespace Smena.Application.Shifts;

/// <summary>
/// Smena može da nosi oznaku šablona (npr. "I") samo ako stvarno ima vreme tog šablona i njegovu poziciju,
/// da oznaka u rasporedu uvek govori istinu.
/// </summary>
internal static class TemplateRules
{
    public static async Task<(ShiftTemplate? Template, Error? Error)> ForShiftAsync(
        IAppDbContext db, Guid templateId, Guid positionId, DateTimeOffset startsAt, DateTimeOffset endsAt, CancellationToken cancellationToken)
    {
        var template = await db.ShiftTemplates.FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken);
        if (template is null)
            return (null, Error.NotFound("templates.not_found", "Šablon smene nije pronađen."));
        if (template.PositionId != positionId)
            return (null, Error.Validation("shifts.template_position", "Šablon pripada drugoj poziciji."));

        // Vreme se poredi po satu u zoni restorana, na minut tačno.
        var zone = await DayOffRules.RestaurantZoneAsync(db, cancellationToken);
        var start = Minute(TimeZoneInfo.ConvertTime(startsAt, zone));
        var end = Minute(TimeZoneInfo.ConvertTime(endsAt, zone));
        return start == template.StartTime && end == template.EndTime
            ? (template, null)
            : (null, Mismatch(template));
    }

    public static (ShiftTemplate? Template, Error? Error) ForSeries(ShiftTemplate? template, Guid positionId, TimeOnly start, TimeOnly end)
    {
        if (template is null)
            return (null, Error.NotFound("templates.not_found", "Šablon smene nije pronađen."));
        if (template.PositionId != positionId)
            return (null, Error.Validation("shifts.template_position", "Šablon pripada drugoj poziciji."));

        return start == template.StartTime && end == template.EndTime
            ? (template, null)
            : (null, Mismatch(template));
    }

    private static Error Mismatch(ShiftTemplate template) =>
        Error.Validation("shifts.template_mismatch",
            $"Vreme smene ne odgovara šablonu {template.Name} ({template.StartTime:HH:mm}-{template.EndTime:HH:mm}).");

    private static TimeOnly Minute(DateTimeOffset local) => new(local.Hour, local.Minute);
}
