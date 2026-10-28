using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Smena.Application.Abstractions;
using Smena.Application.Auth;
using Smena.Application.Common;
using Smena.Domain.Restaurants;

namespace Smena.Application.Positions;

public sealed record CreatePositionCommand(string Name);

public sealed class CreatePositionValidator : AbstractValidator<CreatePositionCommand>
{
    public CreatePositionValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

public sealed class CreatePositionHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IValidator<CreatePositionCommand> validator)
{
    public async Task<Result<PositionDto>> HandleAsync(CreatePositionCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var name = command.Name.Trim();
        if (await db.Positions.AnyAsync(p => p.Name == name, cancellationToken))
            return Error.Conflict("positions.name_taken", "Pozicija sa ovim imenom već postoji.");

        var position = new Position(currentUser.RestaurantId!.Value, name);
        db.Positions.Add(position);
        await db.SaveChangesAsync(cancellationToken);

        return new PositionDto(position.Id, position.Name);
    }
}
