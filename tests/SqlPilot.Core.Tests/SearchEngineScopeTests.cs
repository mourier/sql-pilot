using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using SqlPilot.Core.Database;
using SqlPilot.Core.Scope;
using SqlPilot.Core.Search;
using Xunit;

namespace SqlPilot.Core.Tests
{
    public class SearchEngineScopeTests : IDisposable
    {
        private readonly string _tempFile;
        private readonly SearchScopeStore _scope;
        private readonly SearchEngine _engine;
        private readonly IDatabaseObjectProvider _provider;

        public SearchEngineScopeTests()
        {
            _tempFile = Path.Combine(Path.GetTempPath(), $"sqlpilot_enginescope_{Guid.NewGuid()}.txt");
            _scope = new SearchScopeStore(_tempFile);
            _engine = new SearchEngine(null, null, _scope);
            _provider = Substitute.For<IDatabaseObjectProvider>();

            Stub("server-a", "Sales");
            Stub("server-a", "Warehouse");
            Stub("server-b", "Sales");
        }

        [Fact]
        public async Task SearchAsync_WithEmptyScope_ReturnsObjectsFromEveryDatabase()
        {
            await IndexAllAsync();

            var results = await _engine.SearchAsync("Customer", new SearchFilter());

            results.Should().HaveCount(3);
        }

        [Fact]
        public async Task SearchAsync_SkipsExcludedDatabase()
        {
            await IndexAllAsync();

            _scope.SetDatabaseIncluded("server-a", "Sales", false);

            var results = await _engine.SearchAsync("Customer", new SearchFilter());
            results.Should().NotContain(r => r.Object.ServerName == "server-a" && r.Object.DatabaseName == "Sales");
            results.Should().HaveCount(2);
        }

        [Fact]
        public async Task SearchAsync_SkipsEveryDatabaseOfAnExcludedServer()
        {
            await IndexAllAsync();

            _scope.SetServerIncluded("server-a", false);

            var results = await _engine.SearchAsync("Customer", new SearchFilter());
            results.Should().OnlyContain(r => r.Object.ServerName == "server-b");
        }

        [Fact]
        public async Task SearchAsync_ReturnsDatabaseAgainAfterItIsReIncluded()
        {
            await IndexAllAsync();
            _scope.SetDatabaseIncluded("server-a", "Sales", false);

            _scope.SetDatabaseIncluded("server-a", "Sales", true);

            var results = await _engine.SearchAsync("Customer", new SearchFilter());
            results.Should().HaveCount(3);
        }

        [Fact]
        public async Task SearchAsync_WithoutAScopeStore_ReturnsEverything()
        {
            var engine = new SearchEngine();
            await engine.RefreshIndexAsync("server-a", "Sales", _provider);

            var results = await engine.SearchAsync("Customer", new SearchFilter());

            results.Should().HaveCount(1);
        }

        [Fact]
        public async Task ClearDatabase_RemovesOnlyThatDatabase()
        {
            await IndexAllAsync();

            _engine.ClearDatabase("server-a", "Sales");

            var results = await _engine.SearchAsync("Customer", new SearchFilter());
            results.Should().HaveCount(2);
            results.Should().NotContain(r => r.Object.ServerName == "server-a" && r.Object.DatabaseName == "Sales");
        }

        [Fact]
        public async Task ClearDatabase_LeavesTheSameDatabaseNameOnOtherServersIndexed()
        {
            await IndexAllAsync();

            _engine.ClearDatabase("server-b", "Sales");

            var results = await _engine.SearchAsync("Customer", new SearchFilter());
            results.Should().Contain(r => r.Object.ServerName == "server-a" && r.Object.DatabaseName == "Sales");
        }

        [Fact]
        public async Task ClearDatabase_ForUnknownDatabase_DoesNothing()
        {
            await IndexAllAsync();

            _engine.ClearDatabase("server-a", "NeverIndexed");

            var results = await _engine.SearchAsync("Customer", new SearchFilter());
            results.Should().HaveCount(3);
        }

        [Fact]
        public async Task RefreshIndexAsync_SkipsAnExcludedDatabase()
        {
            _scope.SetDatabaseIncluded("server-a", "Sales", false);

            await IndexAllAsync();

            _scope.SetDatabaseIncluded("server-a", "Sales", true);
            var results = await _engine.SearchAsync("Customer", new SearchFilter());
            results.Should().NotContain(r => r.Object.ServerName == "server-a" && r.Object.DatabaseName == "Sales");
        }

        [Fact]
        public async Task RefreshIndexAsync_SkipsEveryDatabaseOfAnExcludedServer()
        {
            _scope.SetServerIncluded("server-a", false);

            await IndexAllAsync();

            _scope.SetServerIncluded("server-a", true);
            var results = await _engine.SearchAsync("Customer", new SearchFilter());
            results.Should().OnlyContain(r => r.Object.ServerName == "server-b");
        }

        private async Task IndexAllAsync()
        {
            await _engine.RefreshIndexAsync("server-a", "Sales", _provider);
            await _engine.RefreshIndexAsync("server-a", "Warehouse", _provider);
            await _engine.RefreshIndexAsync("server-b", "Sales", _provider);
        }

        private void Stub(string server, string database)
        {
            _provider.GetObjectsAsync(server, database, Arg.Any<CancellationToken>())
                .Returns(new List<DatabaseObject>
                {
                    new DatabaseObject
                    {
                        ServerName = server,
                        DatabaseName = database,
                        SchemaName = "dbo",
                        ObjectName = "Customer",
                        ObjectType = DatabaseObjectType.Table
                    }
                });
        }

        public void Dispose()
        {
            if (File.Exists(_tempFile))
                File.Delete(_tempFile);
        }
    }
}
