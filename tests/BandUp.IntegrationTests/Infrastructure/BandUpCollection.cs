namespace BandUp.IntegrationTests.Infrastructure;

/// <summary>
/// Puts every integration test in one collection so the containers start once for the run
/// instead of once per class.
/// </summary>
[CollectionDefinition(Name)]
public sealed class BandUpCollection : ICollectionFixture<BandUpTestEnvironment>
{
    public const string Name = "bandup";
}