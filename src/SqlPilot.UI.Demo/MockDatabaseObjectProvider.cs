using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SqlPilot.Core.Database;

namespace SqlPilot.UI.Demo
{
    /// <summary>
    /// Fake provider for the demo app. Multi-server on purpose: the scope tree only
    /// gets interesting once there is more than one server and more than a couple of
    /// databases to check and uncheck.
    /// </summary>
    public class MockDatabaseObjectProvider : IDatabaseObjectProvider
    {
        public static readonly IReadOnlyList<string> Servers = new[]
        {
            "localhost\\SQL2019",
            "prod-sql-01",
            "contoso.database.windows.net"
        };

        private static readonly Dictionary<string, List<string>> DatabasesByServer =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["localhost\\SQL2019"] = new List<string> { "AdventureWorks", "Northwind", "ReportServer" },
                ["prod-sql-01"] = new List<string> { "Sales", "Inventory", "Logging", "Staging" },
                ["contoso.database.windows.net"] = new List<string>
                {
                    "billing-prod", "billing-test", "identity", "telemetry", "archive-2024", "archive-2025"
                }
            };

        private static readonly Dictionary<string, List<DatabaseObject>> CuratedData =
            new Dictionary<string, List<DatabaseObject>>(StringComparer.OrdinalIgnoreCase)
            {
                ["AdventureWorks"] = new List<DatabaseObject>
                {
                    MakeObj("AdventureWorks", "HumanResources", "Employee", DatabaseObjectType.Table),
                    MakeObj("AdventureWorks", "HumanResources", "Department", DatabaseObjectType.Table),
                    MakeObj("AdventureWorks", "HumanResources", "EmployeeDepartmentHistory", DatabaseObjectType.Table),
                    MakeObj("AdventureWorks", "Person", "Person", DatabaseObjectType.Table),
                    MakeObj("AdventureWorks", "Person", "Address", DatabaseObjectType.Table),
                    MakeObj("AdventureWorks", "Person", "EmailAddress", DatabaseObjectType.Table),
                    MakeObj("AdventureWorks", "Person", "PhoneNumber", DatabaseObjectType.Table),
                    MakeObj("AdventureWorks", "Sales", "Customer", DatabaseObjectType.Table),
                    MakeObj("AdventureWorks", "Sales", "SalesOrderHeader", DatabaseObjectType.Table),
                    MakeObj("AdventureWorks", "Sales", "SalesOrderDetail", DatabaseObjectType.Table),
                    MakeObj("AdventureWorks", "Sales", "SalesPerson", DatabaseObjectType.Table),
                    MakeObj("AdventureWorks", "Sales", "Store", DatabaseObjectType.Table),
                    MakeObj("AdventureWorks", "Production", "Product", DatabaseObjectType.Table),
                    MakeObj("AdventureWorks", "Production", "ProductCategory", DatabaseObjectType.Table),
                    MakeObj("AdventureWorks", "Production", "ProductSubcategory", DatabaseObjectType.Table),
                    MakeObj("AdventureWorks", "Production", "WorkOrder", DatabaseObjectType.Table),
                    MakeObj("AdventureWorks", "HumanResources", "vEmployee", DatabaseObjectType.View),
                    MakeObj("AdventureWorks", "Sales", "vSalesPerson", DatabaseObjectType.View),
                    MakeObj("AdventureWorks", "Production", "vProductAndDescription", DatabaseObjectType.View),
                    MakeObj("AdventureWorks", "dbo", "uspGetEmployeeManagers", DatabaseObjectType.StoredProcedure),
                    MakeObj("AdventureWorks", "dbo", "uspGetManagerEmployees", DatabaseObjectType.StoredProcedure),
                    MakeObj("AdventureWorks", "dbo", "uspSearchCandidateResumes", DatabaseObjectType.StoredProcedure),
                    MakeObj("AdventureWorks", "dbo", "ufnGetContactInformation", DatabaseObjectType.ScalarFunction),
                    MakeObj("AdventureWorks", "dbo", "ufnGetProductDealerPrice", DatabaseObjectType.ScalarFunction),
                    MakeObj("AdventureWorks", "dbo", "ufnGetProductListPrice", DatabaseObjectType.ScalarFunction),
                },
                ["Northwind"] = new List<DatabaseObject>
                {
                    MakeObj("Northwind", "dbo", "Customers", DatabaseObjectType.Table),
                    MakeObj("Northwind", "dbo", "Orders", DatabaseObjectType.Table),
                    MakeObj("Northwind", "dbo", "OrderDetails", DatabaseObjectType.Table),
                    MakeObj("Northwind", "dbo", "Products", DatabaseObjectType.Table),
                    MakeObj("Northwind", "dbo", "Categories", DatabaseObjectType.Table),
                    MakeObj("Northwind", "dbo", "Suppliers", DatabaseObjectType.Table),
                    MakeObj("Northwind", "dbo", "Employees", DatabaseObjectType.Table),
                    MakeObj("Northwind", "dbo", "Shippers", DatabaseObjectType.Table),
                    MakeObj("Northwind", "dbo", "CustOrderHist", DatabaseObjectType.StoredProcedure),
                    MakeObj("Northwind", "dbo", "CustOrdersDetail", DatabaseObjectType.StoredProcedure),
                    MakeObj("Northwind", "dbo", "SalesByCategory", DatabaseObjectType.StoredProcedure),
                }
            };

        public Task<IReadOnlyList<DatabaseObject>> GetObjectsAsync(
            string serverName, string databaseName, CancellationToken cancellationToken = default)
        {
            var objects = CuratedData.TryGetValue(databaseName, out var curated)
                ? curated
                : Generate(serverName, databaseName);

            return Task.FromResult<IReadOnlyList<DatabaseObject>>(objects);
        }

        public Task<IReadOnlyList<string>> GetDatabaseNamesAsync(
            string serverName, CancellationToken cancellationToken = default)
        {
            var databases = DatabasesByServer.TryGetValue(serverName, out var names)
                ? names
                : new List<string>();

            return Task.FromResult<IReadOnlyList<string>>(databases);
        }

        /// <summary>Filler objects so every database has something findable in it.</summary>
        private static List<DatabaseObject> Generate(string serverName, string databaseName)
        {
            var names = new[] { "Customer", "Order", "Invoice", "AuditLog", "Setting" };

            return names
                .Select(name => new DatabaseObject
                {
                    ServerName = serverName,
                    DatabaseName = databaseName,
                    SchemaName = "dbo",
                    ObjectName = $"{databaseName.Replace("-", "")}{name}",
                    ObjectType = DatabaseObjectType.Table
                })
                .ToList();
        }

        // Both curated databases live on CuratedServer only, so their objects can
        // carry the right server name from the start.
        private const string CuratedServer = @"localhost\SQL2019";

        private static DatabaseObject MakeObj(string db, string schema, string name, DatabaseObjectType type)
        {
            return new DatabaseObject
            {
                ServerName = CuratedServer,
                DatabaseName = db,
                SchemaName = schema,
                ObjectName = name,
                ObjectType = type
            };
        }
    }
}
