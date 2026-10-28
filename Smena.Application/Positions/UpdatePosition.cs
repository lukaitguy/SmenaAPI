using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Auth;
using Smena.Application.Common;

namespace Smena.Application.Positions;

public sealed record UpdatePositionCommand(string Name);

public sealed class UpdatePositionValidator : AbstractValidator<UpdatePositionCommand>
{
    public UpdatePositionValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

public sealed class UpdatePositionHandler(IAppDbContext db, IValidator<UpdatePositionCommand> validator)
{
    public async Task<Result<PositionDto>> HandleAsync(Guid positionId, UpdatePositionCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var position = await db.Positions.FirstOrDefaultAsync(p => p.Id == positionId, cancellationToken);
        if (position is null)
            return Error.NotFound("positions.not_found", "Pozicija nije pronađena.");

        var name = command.Name.Trim();
        if (await db.Positions.AnyAsync(p => p.Id != positionId && p.Name == name, cancellationToken))
            return Error.Conflict("positions.name_taken", "Pozicija sa ovim imenom već postoji.");

        position.Rename(name);
        await db.SaveChangesAsync(cancellationToken);

        return new PositionDto(position.Id, position.Name);
    }
}
