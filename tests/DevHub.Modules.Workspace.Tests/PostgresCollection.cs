using DevHub.TestHarness;
using Xunit;

namespace DevHub.Modules.Workspace.Tests;

[CollectionDefinition(DevHub.TestHarness.PostgresCollection.Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }
