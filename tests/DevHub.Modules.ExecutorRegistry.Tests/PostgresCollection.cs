using DevHub.TestHarness;
using Xunit;

namespace DevHub.Modules.ExecutorRegistry.Tests;

[CollectionDefinition(DevHub.TestHarness.PostgresCollection.Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }
