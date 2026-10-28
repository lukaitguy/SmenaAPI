using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SmenaAPI.Infrastructure;

/// <summary>
/// Pretvara očekivane izuzetke u ProblemDetails: neispravan unos u 400, sukob istovremenih
/// izmena (xmin) u 409. Sve ostalo ostaje 500 bez detalja.
/// </summary>
internal sealed class ExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails? problem = exception switch
        {
            ValidationException validation => new ValidationProblemDetails(
                validation.Errors
                    .GroupBy(e => ToCamelCase(e.PropertyName))
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Neispravan unos.",
            },
            DbUpdateConcurrencyException => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Podatak je u međuvremenu izmenjen.",
                Detail = "Neko je istovremeno izmenio isti podatak. Osveži prikaz i pokušaj ponovo.",
                Extensions = { ["code"] = "concurrency_conflict" },
            },
            _ => null,
        };

        if (problem is null)
            return false;

        context.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static string ToCamelCase(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
