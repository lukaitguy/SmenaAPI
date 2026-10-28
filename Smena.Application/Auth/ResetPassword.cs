using FluentValidation;
using Smena.Application.Abstractions;
using Smena.Application.Common;

namespace Smena.Application.Auth;

public sealed record ResetPasswordCommand(string Email, string Token, string NewPassword);

public sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MaximumLength(128);
    }
}

public sealed class ResetPasswordHandler(
    IIdentityService identity,
    IAuthTokenService tokens,
    IValidator<ResetPasswordCommand> validator)
{
    public async Task<Result> HandleAsync(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var result = await identity.ResetPasswordAsync(
            EmailAddress.Normalize(command.Email), command.Token, command.NewPassword, cancellationToken);
        if (!result.IsSuccess)
            return result.Error!;

        // Ko je znao staru lozinku ne sme da ostane prijavljen.
        await tokens.RevokeAllForUserAsync(result.Value, cancellationToken);
        return Result.Success();
    }
}
