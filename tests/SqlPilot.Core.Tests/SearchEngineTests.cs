using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using SqlPilot.Core.Database;
using SqlPilot.Core.Search;
using Xunit;

namespace SqlPilot.Core.Tests
{
    public class SearchEngineTests
    {
        private readonly SearchEngine _engine;
        private readonly IDatabaseObjectProvider _mockProvider;

        public SearchEngineTests()
        {
            _engine = new SearchEngine();
            _mockProvider = Substitute.For<IDatabaseObjectProvider>();

            var testObjects = new List<DatabaseObject>
            {
                MakeObj("dbo", "Customer", DatabaseObjectType.Table),
                MakeObj("dbo", "CustomerAddress", DatabaseObjectType.Table),
                MakeObj("dbo", "Orders", DatabaseObjectType.Table),
                MakeObj("dbo", "OrderDetails", DatabaseObjectType.Table),
                MakeObj("dbo", "Products", DatabaseObjectType.Table),
                MakeObj("Sales", "SalesOrderHeader", DatabaseObjectType.Table),
                MakeObj("dbo", "vCustomerOrders", DatabaseObjectType.View),
                MakeObj("dbo", "uspGetCustomer", DatabaseObjectType.StoredProcedure),
            };

            _mockProvider.GetObjectsAsync("localhost", "TestDB", Arg.Any<CancellationToken>())
                .Returns(testObjects);
        }

        [Fact]
        public async Task SearchAsync_FindsMatchingObjects()
        {
            await _engine.RefreshIndexAsync("localhost", "TestDB", _mockProvider);

            var results = await _engine.SearchAsync("Cust", new SearchFilter());

            results.Should().NotBeEmpty();
            results.Should().Contain(r => r.Object.ObjectName == "Customer");
        }

        [Fact]
        public async Task SearchAsync_RanksExactMatchHighest()
        {
            await _engine.RefreshIndexAsync("localhost", "TestDB", _mockProvider);

            var results = await _engine.SearchAsync("Customer", new SearchFilter());

            results.Should().NotBeEmpty();
            results[0].Object.ObjectName.Should().Be("Customer");
        }

        [Fact]
        public async Task SearchAsync_RespectsMaxResults()
        {
            await _engine.RefreshIndexAsync("localhost", "TestDB", _mockProvider);

            var filter = new SearchFilter { MaxResults = 2 };
            var results = await _engine.SearchAsync("o", filter);

            results.Count.Should().BeLessOrEqualTo(2);
        }

        [Fact]
        public async Task SearchAsync_FiltersByObjectType()
        {
            await _engine.RefreshIndexAsync("localhost", "TestDB", _mockProvider);

            var filter = new SearchFilter
            {
                ObjectTypes = new[] { DatabaseObjectType.View }
            };
            var results = await _engine.SearchAsync("Customer", filter);

            results.Should().NotBeEmpty();
            results.Should().OnlyContain(r => r.Object.ObjectType == DatabaseObjectType.View);
        }

        [Fact]
        public async Task SearchAsync_FiltersByDatabase()
        {
            await _engine.RefreshIndexAsync("localhost", "TestDB", _mockProvider);

            var filter = new SearchFilter { DatabaseName = "NonExistent" };
            var results = await _engine.SearchAsync("Customer", filter);

            results.Should().BeEmpty();
        }

        [Fact]
        public async Task SearchAsync_SupportsCancellation()
        {
            await _engine.RefreshIndexAsync("localhost", "TestDB", _mockProvider);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var act = async () => await _engine.SearchAsync("Cust", new SearchFilter(), cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task ClearServer_RemovesIndexedObjects()
        {
            await _engine.RefreshIndexAsync("localhost", "TestDB", _mockProvider);

            _engine.ClearServer("localhost");

            var results = await _engine.SearchAsync("Customer", new SearchFilter());
            results.Should().BeEmpty();
        }

        [Fact]
        public async Task ClearAll_RemovesEverything()
        {
            await _engine.RefreshIndexAsync("localhost", "TestDB", _mockProvider);

            _engine.ClearAll();

            var results = await _engine.SearchAsync("Customer", new SearchFilter());
            results.Should().BeEmpty();
        }

        [Fact]
        public async Task SearchAsync_MatchesAcrossSchemaAndName()
        {
            await _engine.RefreshIndexAsync("localhost", "TestDB", _mockProvider);

            // "Sales.SalesOrderHeader" should be findable
            var results = await _engine.SearchAsync("SalesOrder", new SearchFilter());
            results.Should().Contain(r => r.Object.ObjectName == "SalesOrderHeader");
        }

        private static DatabaseObject MakeObj(string schema, string name, DatabaseObjectType type)
        {
            return new DatabaseObject
            {
                ServerName = "localhost",
                DatabaseName = "TestDB",
                SchemaName = schema,
                ObjectName = name,
                ObjectType = type
            };
        }
    }
}
