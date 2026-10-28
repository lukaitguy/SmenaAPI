using FluentValidation;
using Smena.Application.Abstractions;
using Smena.Application.Common;
using Smena.Domain.Employees;
using Smena.Domain.Restaurants;

namespace Smena.Application.Auth;

public sealed record RegisterRestaurantCommand(
    string RestaurantName,
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string? TimeZoneId);

public sealed class RegisterRestaurantValidator : AbstractValidator<RegisterRestaurantCommand>
{
    public RegisterRestaurantValidator()
    {
        RuleFor(x => x.RestaurantName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
        RuleFor(x => x.TimeZoneId)
            .Must(id => TimeZoneInfo.TryFindSystemTimeZoneById(id!, out _))
            .When(x => x.TimeZoneId is not null)
            .WithMessage("Nepoznata vremenska zona.");
    }
}

/// <summary>Novi restoran sa vlasnikom i podrazumevanim pozicijama, u jednoj transakciji.</summary>
public sealed class RegisterRestaurantHandler(
    IAppDbContext db,
    IIdentityService identity,
    IAuthTokenService tokens,
    IValidator<RegisterRestaurantCommand> validator,
    TimeProvider time)
{
    public async Task<Result<AuthTokens>> HandleAsync(RegisterRestaurantCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var email = EmailAddress.Normalize(command.Email);
        if (await identity.EmailExistsAsync(email, cancellationToken))
            return Error.Conflict("auth.email_taken", "Nalog sa ovim emailom već postoji.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var user = await identity.CreateUserAsync(email, command.Password, cancellationToken);
        if (!user.IsSuccess)
            return user.Error!;

        var restaurant = new Restaurant(
            command.RestaurantName.Trim(),
            command.TimeZoneId ?? Restaurant.DefaultTimeZoneId,
            time.GetUtcNow());
        db.Restaurants.Add(restaurant);
        db.Positions.AddRange(Position.Defaults.Select(name => new Position(restaurant.Id, name)));

        var owner = new Employee(
            restaurant.Id, user.Value, command.FirstName.Trim(), command.LastName.Trim(), EmployeeRole.Owner);
        db.Employees.Add(owner);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await tokens.IssueAsync(owner, cancellationToken);
    }
}
