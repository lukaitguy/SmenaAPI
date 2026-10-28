using Smena.Domain.Common;

namespace Smena.Domain.Restaurants;

/// <summary>
/// Uloga u smeni (konobar, šanker, kuvar...). Nije isto što i dozvola u sistemu,
/// za to služi <see cref="Employees.EmployeeRole"/>.
/// </summary>
public class Position : IRestaurantScoped
{
    public static readonly string[] Defaults = ["Konobar", "Šanker", "Kuvar", "Hostesa"];

    private Position() { }

    public Position(Guid restaurantId, string name)
    {
        Id = Guid.CreateVersion7();
        RestaurantId = restaurantId;
        Name = name;
    }

    public Guid Id { get; private set; }
    public Guid RestaurantId { get; private set; }
    public string Name { get; private set; } = null!;

    public void Rename(string name) => Name = name;
}
