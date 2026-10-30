using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Application.Shifts;
using Smena.Domain.Shifts;

namespace Smena.Application.Templates;

public sealed record ShiftTemplateDto(
    Guid Id,
    Guid PositionId,
    string PositionName,
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    bool EndsNextDay);

internal static class ShiftTemplateQueries
{
    public static IQueryable<ShiftTemplateDto> ProjectToDto(this IAppDbContext db, IQueryable<ShiftTemplate> templates) =>
        from t in templates
        join p in db.Positions on t.PositionId equals p.Id
        orderby p.Name, t.StartTime
        select new ShiftTemplateDto(t.Id, p.Id, p.Name, t.Name, t.StartTime, t.EndTime, t.EndTime <= t.StartTime);
}

public sealed class ListShiftTemplatesHandler(IAppDbContext db)
{
    /// <summary>Svi šabloni restorana, po poziciji. Čitaju ih svi zaposleni, menja samo menadžer.</summary>
    public async Task<IReadOnlyList<ShiftTemplateDto>> HandleAsync(CancellationToken cancellationToken) =>
        await db.ProjectToDto(db.ShiftTemplates).ToListAsync(cancellationToken);
}

public sealed record CreateShiftTemplateCommand(Guid PositionId, string Name, TimeOnly StartTime, TimeOnly EndTime);

public sealed record UpdateShiftTemplateCommand(string Name, TimeOnly StartTime, TimeOnly EndTime);

public sealed class CreateShiftTemplateValidator : AbstractValidator<CreateShiftTemplateCommand>
{
    public CreateShiftTemplateValidator()
    {
        RuleFor(x => x.PositionId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(ShiftTemplate.MaxNameLength);
        RuleFor(x => x.EndTime).NotEqual(x => x.StartTime).WithMessage("Početak i kraj smene ne mogu biti isti.");
        RuleFor(x => x).Must(x => RecurrencePlanner.NominalDuration(x.StartTime, x.EndTime) <= Shift.MaxDuration)
            .WithName(nameof(CreateShiftTemplateCommand.EndTime))
            .WithMessage($"Smena ne može trajati duže od {Shift.MaxDuration.TotalHours} sati.");
    }
}

public sealed class UpdateShiftTemplateValidator : AbstractValidator<UpdateShiftTemplateCommand>
{
    public UpdateShiftTemplateValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(ShiftTemplate.MaxNameLength);
        RuleFor(x => x.EndTime).NotEqual(x => x.StartTime).WithMessage("Početak i kraj smene ne mogu biti isti.");
        RuleFor(x => x).Must(x => RecurrencePlanner.NominalDuration(x.StartTime, x.EndTime) <= Shift.MaxDuration)
            .WithName(nameof(UpdateShiftTemplateCommand.EndTime))
            .WithMessage($"Smena ne može trajati duže od {Shift.MaxDuration.TotalHours} sati.");
    }
}

public sealed class CreateShiftTemplateHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IValidator<CreateShiftTemplateCommand> validator)
{
    public async Task<Result<ShiftTemplateDto>> HandleAsync(CreateShiftTemplateCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        if (!await db.Positions.AnyAsync(p => p.Id == command.PositionId, cancellationToken))
            return Error.NotFound("positions.not_found", "Pozicija nije pronađena.");

        var name = command.Name.Trim();
        if (await db.ShiftTemplates.AnyAsync(t => t.PositionId == command.PositionId && t.Name == name, cancellationToken))
            return Error.Conflict("templates.name_taken", "Ova pozicija već ima smenu sa tim nazivom.");

        var template = new ShiftTemplate(currentUser.RestaurantId!.Value, command.PositionId, name, command.StartTime, command.EndTime);
        db.ShiftTemplates.Add(template);
        await db.SaveChangesAsync(cancellationToken);

        return await db.ProjectToDto(db.ShiftTemplates.Where(t => t.Id == template.Id)).FirstAsync(cancellationToken);
    }
}

public sealed class UpdateShiftTemplateHandler(IAppDbContext db, IValidator<UpdateShiftTemplateCommand> validator)
{
    /// <summary>Menja šablon za ubuduće: već napravljene smene zadržavaju svoje vreme.</summary>
    public async Task<Result<ShiftTemplateDto>> HandleAsync(Guid id, UpdateShiftTemplateCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var template = await db.ShiftTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template is null)
            return Error.NotFound("templates.not_found", "Šablon smene nije pronađen.");

        var name = command.Name.Trim();
        if (await db.ShiftTemplates.AnyAsync(t => t.Id != id && t.PositionId == template.PositionId && t.Name == name, cancellationToken))
            return Error.Conflict("templates.name_taken", "Ova pozicija već ima smenu sa tim nazivom.");

        template.Update(name, command.StartTime, command.EndTime);
        await db.SaveChangesAsync(cancellationToken);

        return await db.ProjectToDto(db.ShiftTemplates.Where(t => t.Id == id)).FirstAsync(cancellationToken);
    }
}

public sealed class DeleteShiftTemplateHandler(IAppDbContext db)
{
    /// <summary>Smene napravljene po šablonu ostaju, samo gube oznaku (npr. "I") i prikazuju se kao proizvoljno vreme.</summary>
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var template = await db.ShiftTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template is null)
            return Error.NotFound("templates.not_found", "Šablon smene nije pronađen.");

        db.ShiftTemplates.Remove(template);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
