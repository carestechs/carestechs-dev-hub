using Xunit;

namespace DevHub.TestHarness;

/// <summary>
/// Test classes that need the shared Postgres container opt in with <c>[Collection(PostgresCollection.Name)]</c>.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
