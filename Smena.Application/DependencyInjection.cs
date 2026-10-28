using System.Globalization;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Smena.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        // Ručni handleri umesto MediatR-a: svaka klasa *Handler se registruje kao scoped
        // i kontroler je dobija direktno kroz DI.
        foreach (var handler in assembly.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false } && t.Name.EndsWith("Handler")))
        {
            services.AddScoped(handler);
        }

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        ValidatorOptions.Global.LanguageManager.Culture = new CultureInfo("sr-Latn");
        services.AddSingleton(TimeProvider.System);
        services.AddMemoryCache();

        return services;
    }
}
