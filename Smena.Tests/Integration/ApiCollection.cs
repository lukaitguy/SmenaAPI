namespace Smena.Tests.Integration;

/// <summary>Jedan server i jedna migracija baze za sve integracione testove.</summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
