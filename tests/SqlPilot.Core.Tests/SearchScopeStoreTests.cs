using System;
using System.IO;
using FluentAssertions;
using SqlPilot.Core.Scope;
using Xunit;

namespace SqlPilot.Core.Tests
{
    public class SearchScopeStoreTests : IDisposable
    {
        private readonly string _tempFile;
        private readonly SearchScopeStore _store;

        public SearchScopeStoreTests()
        {
            _tempFile = Path.Combine(Path.GetTempPath(), $"sqlpilot_scope_{Guid.NewGuid()}.txt");
            _store = new SearchScopeStore(_tempFile);
        }

        [Fact]
        public void UnknownServer_IsIncludedByDefault()
        {
            _store.IsServerIncluded("localhost").Should().BeTrue();
        }

        [Fact]
        public void UnknownDatabase_IsIncludedByDefault()
        {
            _store.IsDatabaseIncluded("localhost", "TestDB").Should().BeTrue();
        }

        [Fact]
        public void SetDatabaseIncluded_False_ExcludesThatDatabase()
        {
            _store.SetDatabaseIncluded("localhost", "TestDB", false);

            _store.IsDatabaseIncluded("localhost", "TestDB").Should().BeFalse();
        }

        [Fact]
        public void SetDatabaseIncluded_False_LeavesSiblingDatabasesIncluded()
        {
            _store.SetDatabaseIncluded("localhost", "TestDB", false);

            _store.IsDatabaseIncluded("localhost", "OtherDB").Should().BeTrue();
        }

        [Fact]
        public void SetDatabaseIncluded_False_LeavesSameNameOnOtherServerIncluded()
        {
            _store.SetDatabaseIncluded("localhost", "TestDB", false);

            _store.IsDatabaseIncluded("other-server", "TestDB").Should().BeTrue();
        }

        [Fact]
        public void SetDatabaseIncluded_BackToTrue_ReIncludesDatabase()
        {
            _store.SetDatabaseIncluded("localhost", "TestDB", false);

            _store.SetDatabaseIncluded("localhost", "TestDB", true);

            _store.IsDatabaseIncluded("localhost", "TestDB").Should().BeTrue();
            _store.GetExcludedDatabases("localhost").Should().BeEmpty();
        }

        [Fact]
        public void SetServerIncluded_False_ExcludesServer()
        {
            _store.SetServerIncluded("localhost", false);

            _store.IsServerIncluded("localhost").Should().BeFalse();
        }

        [Fact]
        public void ExcludedServer_ExcludesAllItsDatabases()
        {
            _store.SetServerIncluded("localhost", false);

            _store.IsDatabaseIncluded("localhost", "AnyDatabase").Should().BeFalse();
        }

        [Fact]
        public void SetServerIncluded_BackToTrue_KeepsIndividualDatabaseExclusions()
        {
            _store.SetDatabaseIncluded("localhost", "TestDB", false);
            _store.SetServerIncluded("localhost", false);

            _store.SetServerIncluded("localhost", true);

            _store.IsServerIncluded("localhost").Should().BeTrue();
            _store.IsDatabaseIncluded("localhost", "OtherDB").Should().BeTrue();
            _store.IsDatabaseIncluded("localhost", "TestDB").Should().BeFalse();
        }

        [Fact]
        public void ServerNames_AreCaseInsensitive()
        {
            _store.SetServerIncluded("LOCALHOST", false);

            _store.IsServerIncluded("localhost").Should().BeFalse();
        }

        [Fact]
        public void DatabaseNames_AreCaseInsensitive()
        {
            _store.SetDatabaseIncluded("LocalHost", "TESTDB", false);

            _store.IsDatabaseIncluded("localhost", "testdb").Should().BeFalse();
        }

        [Fact]
        public void GetExcludedServers_ReturnsOnlyExcludedServers()
        {
            _store.SetServerIncluded("server-a", false);
            _store.SetServerIncluded("server-b", true);

            _store.GetExcludedServers().Should().BeEquivalentTo(new[] { "server-a" });
        }

        [Fact]
        public void GetExcludedDatabases_ReturnsOnlyThatServersExclusions()
        {
            _store.SetDatabaseIncluded("server-a", "DbOne", false);
            _store.SetDatabaseIncluded("server-a", "DbTwo", false);
            _store.SetDatabaseIncluded("server-b", "DbThree", false);

            _store.GetExcludedDatabases("server-a").Should().BeEquivalentTo(new[] { "DbOne", "DbTwo" });
        }

        [Fact]
        public void GetExcludedDatabases_ForUnknownServer_IsEmpty()
        {
            _store.GetExcludedDatabases("never-seen").Should().BeEmpty();
        }

        [Fact]
        public void SaveAndLoad_PersistsServerAndDatabaseExclusions()
        {
            _store.SetServerIncluded("server-a", false);
            _store.SetDatabaseIncluded("server-b", "TestDB", false);
            _store.Save();

            var reloaded = new SearchScopeStore(_tempFile);
            reloaded.Load();

            reloaded.IsServerIncluded("server-a").Should().BeFalse();
            reloaded.IsDatabaseIncluded("server-b", "TestDB").Should().BeFalse();
            reloaded.IsDatabaseIncluded("server-b", "OtherDB").Should().BeTrue();
        }

        [Fact]
        public void SaveAndLoad_PersistsNamesContainingTheSeparator()
        {
            _store.SetDatabaseIncluded(@"sql\instance|odd", "Db|Name", false);
            _store.Save();

            var reloaded = new SearchScopeStore(_tempFile);
            reloaded.Load();

            reloaded.IsDatabaseIncluded(@"sql\instance|odd", "Db|Name").Should().BeFalse();
            reloaded.IsDatabaseIncluded(@"sql\instance|odd", "Other").Should().BeTrue();
        }

        [Fact]
        public void Save_WritesNothingForIncludedItems()
        {
            _store.SetServerIncluded("server-a", true);
            _store.SetDatabaseIncluded("server-a", "TestDB", true);
            _store.Save();

            File.ReadAllText(_tempFile).Trim().Should().BeEmpty();
        }

        [Fact]
        public void Load_WithNoFile_DoesNotThrowAndIncludesEverything()
        {
            var store = new SearchScopeStore(Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid()}.txt"));

            var act = () => store.Load();

            act.Should().NotThrow();
            store.IsDatabaseIncluded("localhost", "TestDB").Should().BeTrue();
        }

        [Fact]
        public void Load_ReplacesInMemoryState()
        {
            _store.SetServerIncluded("stale-server", false);
            _store.Save();
            _store.SetServerIncluded("stale-server", true);
            _store.SetServerIncluded("unsaved-server", false);

            _store.Load();

            _store.IsServerIncluded("stale-server").Should().BeFalse();
            _store.IsServerIncluded("unsaved-server").Should().BeTrue();
        }

        [Fact]
        public void SetDatabaseIncluded_RaisesScopeChanged()
        {
            int raised = 0;
            _store.ScopeChanged += (s, e) => raised++;

            _store.SetDatabaseIncluded("localhost", "TestDB", false);

            raised.Should().Be(1);
        }

        [Fact]
        public void SetServerIncluded_RaisesScopeChanged()
        {
            int raised = 0;
            _store.ScopeChanged += (s, e) => raised++;

            _store.SetServerIncluded("localhost", false);

            raised.Should().Be(1);
        }

        [Fact]
        public void Set_WithNoActualChange_DoesNotRaiseScopeChanged()
        {
            int raised = 0;
            _store.ScopeChanged += (s, e) => raised++;

            _store.SetServerIncluded("localhost", true);
            _store.SetDatabaseIncluded("localhost", "TestDB", true);

            raised.Should().Be(0);
        }

        [Fact]
        public void Load_RaisesScopeChanged()
        {
            _store.SetServerIncluded("server-a", false);
            _store.Save();
            int raised = 0;
            _store.ScopeChanged += (s, e) => raised++;

            _store.Load();

            raised.Should().Be(1);
        }

        [Fact]
        public void NullOrEmptyNames_AreTreatedAsIncluded()
        {
            _store.IsServerIncluded(null).Should().BeTrue();
            _store.IsDatabaseIncluded("localhost", null).Should().BeTrue();
            _store.IsDatabaseIncluded(null, "TestDB").Should().BeTrue();
        }

        public void Dispose()
        {
            if (File.Exists(_tempFile))
                File.Delete(_tempFile);
        }
    }
}
