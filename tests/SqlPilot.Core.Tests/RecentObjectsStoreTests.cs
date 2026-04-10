using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using SqlPilot.Core.Database;
using SqlPilot.Core.Recents;
using Xunit;

namespace SqlPilot.Core.Tests
{
    public class RecentObjectsStoreTests : IDisposable
    {
        private readonly string _tempFile;
        private readonly RecentObjectsStore _store;

        public RecentObjectsStoreTests()
        {
            _tempFile = Path.Combine(Path.GetTempPath(), $"sqlpilot_test_{Guid.NewGuid()}.json");
            _store = new RecentObjectsStore(_tempFile, capacity: 5);
        }

        [Fact]
        public void RecordAccess_MakesObjectRecent()
        {
            var obj = MakeObj("Customer");

            _store.RecordAccess(obj);

            _store.IsRecent(obj).Should().BeTrue();
        }

        [Fact]
        public void RecordAccess_MovesToFront()
        {
            _store.RecordAccess(MakeObj("A"));
            _store.RecordAccess(MakeObj("B"));
            _store.RecordAccess(MakeObj("A")); // Move A back to front

            var recents = _store.GetRecent();
            recents[0].ObjectName.Should().Be("A");
        }

        [Fact]
        public void GetRecent_RespectsMaxItems()
        {
            for (int i = 0; i < 10; i++)
                _store.RecordAccess(MakeObj($"Obj{i}"));

            var recents = _store.GetRecent(3);
            recents.Should().HaveCount(3);
        }

        [Fact]
        public void Capacity_EvictsOldest()
        {
            // Capacity is 5
            for (int i = 0; i < 7; i++)
                _store.RecordAccess(MakeObj($"Obj{i}"));

            _store.IsRecent(MakeObj("Obj0")).Should().BeFalse(); // Evicted
            _store.IsRecent(MakeObj("Obj1")).Should().BeFalse(); // Evicted
            _store.IsRecent(MakeObj("Obj6")).Should().BeTrue();  // Most recent
        }

        [Fact]
        public void SaveAndLoad_PersistsRecents()
        {
            _store.RecordAccess(MakeObj("Customer"));
            _store.RecordAccess(MakeObj("Orders"));
            _store.Save();

            var newStore = new RecentObjectsStore(_tempFile);
            newStore.Load();

            newStore.GetRecent().Should().HaveCount(2);
        }

        [Fact]
        public void GetRecent_ReturnsMostRecentFirst()
        {
            _store.RecordAccess(MakeObj("A"));
            _store.RecordAccess(MakeObj("B"));
            _store.RecordAccess(MakeObj("C"));

            var recents = _store.GetRecent();
            recents.Select(r => r.ObjectName).Should().ContainInOrder("C", "B", "A");
        }

        public void Dispose()
        {
            if (File.Exists(_tempFile))
                File.Delete(_tempFile);
        }

        private static DatabaseObject MakeObj(string name)
        {
            return new DatabaseObject
            {
                ServerName = "localhost",
                DatabaseName = "TestDB",
                SchemaName = "dbo",
                ObjectName = name,
                ObjectType = DatabaseObjectType.Table
            };
        }
    }
}
