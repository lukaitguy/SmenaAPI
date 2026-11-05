using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Domain.EightySix;
using Smena.Domain.Employees;

namespace Smena.Application.EightySix;

public sealed record EightySixItemDto(
    Guid Id,
    Guid PositionId,
    string PositionName,
    string Name,
    string? Note,
    Guid CreatedByEmployeeId,
    string CreatedByName,
    DateTimeOffset CreatedAt);

internal static class EightySixQueries
{
    public static IQueryable<EightySixItemDto> ProjectToDto(this IAppDbContext db, IQueryable<EightySixItem> items) =>
        from i in items
        join p in db.Positions on i.PositionId equals p.Id
        join e in db.Employees on i.CreatedByEmployeeId equals e.Id
        orderby p.Name, i.CreatedAt descending
        select new EightySixItemDto(i.Id, p.Id, p.Name, i.Name, i.Note, e.Id, e.FirstName + " " + e.LastName, i.CreatedAt);

    /// <summary>Menadžer i vlasnik menjaju sve odeljke, a ostali samo odeljak pozicije koju imaju (kuvari i šankeri).</summary>
    public static async Task<bool> CanEditAsync(IAppDbContext db, ICurrentUser currentUser, Guid positionId, CancellationToken cancellationToken)
    {
        var me = currentUser.EmployeeId;
        var employee = await db.Employees
            .Where(e => e.Id == me)
            .Select(e => new { e.Role, HasPosition = e.Positions.Any(p => p.Id == positionId) })
            .FirstOrDefaultAsync(cancellationToken);
        return employee is not null && (employee.Role is EmployeeRole.Manager or EmployeeRole.Owner || employee.HasPosition);
    }
}

public sealed record AddEightySixItemCommand(Guid PositionId, string Name, string? Note);

public sealed class AddEightySixItemValidator : AbstractValidator<AddEightySixItemCommand>
{
    public AddEightySixItemValidator()
    {
        RuleFor(x => x.PositionId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(EightySixItem.MaxNameLength);
        RuleFor(x => x.Note).MaximumLength(EightySixItem.MaxNoteLength);
    }
}

public sealed class AddEightySixItemHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IValidator<AddEightySixItemCommand> validator,
    TimeProvider time)
{
    public async Task<Result<EightySixItemDto>> HandleAsync(AddEightySixItemCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var section = await db.Positions.Where(p => p.Id == command.PositionId).Select(p => p.Name).FirstOrDefaultAsync(cancellationToken);
        if (section is null)
            return Error.NotFound("positions.not_found", "Pozicija nije pronađena.");
        if (!EightySixItem.SectionPositionNames.Contains(section))
            return Error.Validation("eightysix.invalid_section", "86 lista postoji samo za kuhinju (Kuvar) i šank (Šanker).");
        if (!await EightySixQueries.CanEditAsync(db, currentUser, command.PositionId, cancellationToken))
            return Error.Forbidden("eightysix.forbidden", "Listu menjaju kuvari, šankeri i menadžer.");

        var name = command.Name.Trim();
        var exists = await db.EightySixItems.AnyAsync(
            i => i.PositionId == command.PositionId && i.Name.ToLower() == name.ToLower(), cancellationToken);
        if (exists)
            return Error.Conflict("eightysix.already_listed", "Ta stavka je već na listi.");

        var item = new EightySixItem(currentUser.RestaurantId!.Value, command.PositionId, currentUser.EmployeeId!.Value, name, command.Note, time.GetUtcNow());
        db.EightySixItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        return await db.ProjectToDto(db.EightySixItems.Where(i => i.Id == item.Id)).FirstAsync(cancellationToken);
    }
}

public sealed class RemoveEightySixItemHandler(IAppDbContext db, ICurrentUser currentUser)
{
    /// <summary>Stavka se vratila u ponudu: skida se sa liste.</summary>
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await db.EightySixItems.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
            return Error.NotFound("eightysix.not_found", "Stavka nije pronađena.");
        if (!await EightySixQueries.CanEditAsync(db, currentUser, item.PositionId, cancellationToken))
            return Error.Forbidden("eightysix.forbidden", "Listu menjaju kuvari, šankeri i menadžer.");

        db.EightySixItems.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed class ListEightySixHandler(IAppDbContext db)
{
    /// <summary>Cela lista restorana; vide je svi zaposleni da znaju šta nema.</summary>
    public async Task<IReadOnlyList<EightySixItemDto>> HandleAsync(CancellationToken cancellationToken) =>
        await db.ProjectToDto(db.EightySixItems).ToListAsync(cancellationToken);
}
