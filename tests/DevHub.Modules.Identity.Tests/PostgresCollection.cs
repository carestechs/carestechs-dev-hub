using DevHub.TestHarness;
using Xunit;

namespace DevHub.Modules.Identity.Tests;

[CollectionDefinition(DevHub.TestHarness.PostgresCollection.Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }
