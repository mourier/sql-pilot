using System;
using System.IO;
using FluentAssertions;
using SqlPilot.Core.Database;
using SqlPilot.Core.Favorites;
using Xunit;

namespace SqlPilot.Core.Tests
{
    public class FavoritesStoreTests : IDisposable
    {
        private readonly string _tempFile;
        private readonly FavoritesStore _store;

        public FavoritesStoreTests()
        {
            _tempFile = Path.Combine(Path.GetTempPath(), $"sqlpilot_test_{Guid.NewGuid()}.json");
            _store = new FavoritesStore(_tempFile);
        }

        [Fact]
        public void AddFavorite_MakesObjectFavorite()
        {
            var obj = MakeObj("Customer");

            _store.AddFavorite(obj);

            _store.IsFavorite(obj).Should().BeTrue();
        }

        [Fact]
        public void RemoveFavorite_RemovesFromStore()
        {
            var obj = MakeObj("Customer");
            _store.AddFavorite(obj);

            _store.RemoveFavorite(obj);

            _store.IsFavorite(obj).Should().BeFalse();
        }

        [Fact]
        public void GetAll_ReturnsAllFavorites()
        {
            _store.AddFavorite(MakeObj("Customer"));
            _store.AddFavorite(MakeObj("Orders"));

            _store.GetAll().Should().HaveCount(2);
        }

        [Fact]
        public void SaveAndLoad_PersistsFavorites()
        {
            _store.AddFavorite(MakeObj("Customer"));
            _store.AddFavorite(MakeObj("Orders"));
            _store.Save();

            var newStore = new FavoritesStore(_tempFile);
            newStore.Load();

            newStore.GetAll().Should().HaveCount(2);
        }

        [Fact]
        public void Load_WithNoFile_DoesNotThrow()
        {
            var store = new FavoritesStore(Path.Combine(Path.GetTempPath(), "nonexistent.json"));

            var act = () => store.Load();
            act.Should().NotThrow();
            store.GetAll().Should().BeEmpty();
        }

        [Fact]
        public void AddDuplicate_DoesNotCreateDuplicates()
        {
            var obj = MakeObj("Customer");

            _store.AddFavorite(obj);
            _store.AddFavorite(obj);

            _store.GetAll().Should().HaveCount(1);
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
