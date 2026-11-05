using Smena.Application.EightySix;

namespace Smena.Application.Abstractions;

/// <summary>
/// Gura promene ekranima svih prijavljenih zaposlenih restorana (SignalR). Baza je izvor istine: poruka je samo
/// signal da ekran osveži prikaz, a klijent koji je propustio poruku posle ponovnog povezivanja povuče ceo snimak stanja.
/// Greška slanja nikad ne sme da obori operaciju koja je već sačuvana, pa implementacija ne baca izuzetke.
/// </summary>
public interface IBoardNotifier
{
    Task EightySixAddedAsync(Guid restaurantId, EightySixItemDto item, CancellationToken cancellationToken);

    Task EightySixRemovedAsync(Guid restaurantId, Guid itemId, CancellationToken cancellationToken);
}
