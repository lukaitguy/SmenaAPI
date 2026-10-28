using Smena.Domain.Common;
using Smena.Domain.Restaurants;

namespace Smena.Domain.Employees;

public class Employee : IRestaurantScoped
{
    private readonly List<Position> _positions = [];

    private Employee() { }

    public Employee(Guid restaurantId, Guid userId, string firstName, string lastName, EmployeeRole role)
    {
        Id = Guid.CreateVersion7();
        RestaurantId = restaurantId;
        UserId = userId;
        FirstName = firstName;
        LastName = lastName;
        Role = role;
        Status = EmployeeStatus.Active;
    }

    public Guid Id { get; private set; }
    public Guid RestaurantId { get; private set; }

    /// <summary>Id ASP.NET Identity korisnika; Domain namerno ne zna za Identity tipove.</summary>
    public Guid UserId { get; private set; }

    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;
    public EmployeeRole Role { get; private set; }
    public EmployeeStatus Status { get; private set; }

    public IReadOnlyCollection<Position> Positions => _positions;

    public bool CanManage => Role is EmployeeRole.Manager or EmployeeRole.Owner;

    public void AssignPosition(Position position)
    {
        if (position.RestaurantId != RestaurantId)
            throw new InvalidOperationException("Pozicija pripada drugom restoranu.");

        if (_positions.All(p => p.Id != position.Id))
            _positions.Add(position);
    }

    public void Rename(string firstName, string lastName)
    {
        FirstName = firstName;
        LastName = lastName;
    }

    public void ChangeRole(EmployeeRole role) => Role = role;

    public void ReplacePositions(IEnumerable<Position> positions)
    {
        _positions.Clear();
        foreach (var position in positions)
            AssignPosition(position);
    }

    public void Deactivate() => Status = EmployeeStatus.Inactive;

    public void Activate() => Status = EmployeeStatus.Active;
}
