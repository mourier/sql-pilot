using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SqlPilot.Core.Database;

namespace SqlPilot.UI.Demo
{
    public class MockDatabaseObjectProvider : IDatabaseObjectProvider
    {
        private static readonly Dictionary<string, List<DatabaseObject>> MockData = new Dictionary<string, List<DatabaseObject>>
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
            if (MockData.TryGetValue(databaseName, out var objects))
                return Task.FromResult<IReadOnlyList<DatabaseObject>>(objects);

            return Task.FromResult<IReadOnlyList<DatabaseObject>>(new List<DatabaseObject>());
        }

        public Task<IReadOnlyList<string>> GetDatabaseNamesAsync(
            string serverName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<string>>(new List<string> { "AdventureWorks", "Northwind" });
        }

        private static DatabaseObject MakeObj(string db, string schema, string name, DatabaseObjectType type)
        {
            return new DatabaseObject
            {
                ServerName = "localhost",
                DatabaseName = db,
                SchemaName = schema,
                ObjectName = name,
                ObjectType = type
            };
        }
    }
}
