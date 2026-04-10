using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using SqlPilot.Core.Database;

namespace SqlPilot.Smo
{
    public sealed class SmoDatabaseObjectProvider : IDatabaseObjectProvider
    {
        private readonly ConnectionDescriptor _connection;

        public SmoDatabaseObjectProvider(ConnectionDescriptor connection = null)
        {
            _connection = connection;
        }

        private Server CreateServer()
        {
            if (_connection == null) return null;

            // Prefer individual properties — avoids connection string keyword incompatibilities
            // between SSMS versions (e.g. SSMS 20's "Multiple Active Result Sets" with spaces
            // isn't valid for SMO's older ServerConnection parser).
            if (!string.IsNullOrEmpty(_connection.ServerName))
            {
                var serverConn = new ServerConnection();
                serverConn.ServerInstance = _connection.ServerName;
                serverConn.TrustServerCertificate = true;

                if (!_connection.UseIntegratedSecurity)
                {
                    serverConn.LoginSecure = false;
                    serverConn.Login = _connection.UserName ?? "";
                    serverConn.Password = _connection.Password ?? "";
                }

                return new Server(serverConn);
            }

            // Last resort: use the raw connection string (may fail on SSMS 20)
            if (!string.IsNullOrEmpty(_connection.ConnectionString))
            {
                var serverConn = new ServerConnection();
                serverConn.ConnectionString = _connection.ConnectionString;
                serverConn.TrustServerCertificate = true;
                return new Server(serverConn);
            }

            return null;
        }

        public Task<IReadOnlyList<DatabaseObject>> GetObjectsAsync(
            string serverName,
            string databaseName,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                var results = new List<DatabaseObject>();
                var server = CreateServer();
                if (server == null) return (IReadOnlyList<DatabaseObject>)results;

                server.SetDefaultInitFields(typeof(Table), "Name", "Schema", "IsSystemObject");
                server.SetDefaultInitFields(typeof(View), "Name", "Schema", "IsSystemObject");
                server.SetDefaultInitFields(typeof(StoredProcedure), "Name", "Schema", "IsSystemObject");
                server.SetDefaultInitFields(typeof(UserDefinedFunction), "Name", "Schema", "IsSystemObject", "FunctionType");
                server.SetDefaultInitFields(typeof(Synonym), "Name", "Schema");

                var db = server.Databases[databaseName];
                if (db == null) return (IReadOnlyList<DatabaseObject>)results;

                cancellationToken.ThrowIfCancellationRequested();

                foreach (Table table in db.Tables)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (table.IsSystemObject) continue;
                    results.Add(new DatabaseObject
                    {
                        ServerName = serverName, DatabaseName = databaseName,
                        SchemaName = table.Schema, ObjectName = table.Name,
                        ObjectType = DatabaseObjectType.Table
                    });
                }

                foreach (View view in db.Views)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (view.IsSystemObject) continue;
                    results.Add(new DatabaseObject
                    {
                        ServerName = serverName, DatabaseName = databaseName,
                        SchemaName = view.Schema, ObjectName = view.Name,
                        ObjectType = DatabaseObjectType.View
                    });
                }

                foreach (StoredProcedure sp in db.StoredProcedures)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (sp.IsSystemObject) continue;
                    results.Add(new DatabaseObject
                    {
                        ServerName = serverName, DatabaseName = databaseName,
                        SchemaName = sp.Schema, ObjectName = sp.Name,
                        ObjectType = DatabaseObjectType.StoredProcedure
                    });
                }

                foreach (UserDefinedFunction fn in db.UserDefinedFunctions)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (fn.IsSystemObject) continue;
                    var objType = fn.FunctionType == UserDefinedFunctionType.Scalar
                        ? DatabaseObjectType.ScalarFunction
                        : DatabaseObjectType.TableValuedFunction;
                    results.Add(new DatabaseObject
                    {
                        ServerName = serverName, DatabaseName = databaseName,
                        SchemaName = fn.Schema, ObjectName = fn.Name,
                        ObjectType = objType
                    });
                }

                // Synonyms have no IsSystemObject property — SQL Server doesn't ship built-in synonyms
                foreach (Synonym syn in db.Synonyms)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    results.Add(new DatabaseObject
                    {
                        ServerName = serverName, DatabaseName = databaseName,
                        SchemaName = syn.Schema, ObjectName = syn.Name,
                        ObjectType = DatabaseObjectType.Synonym
                    });
                }

                return (IReadOnlyList<DatabaseObject>)results;
            }, cancellationToken);
        }

        public Task<IReadOnlyList<string>> GetDatabaseNamesAsync(
            string serverName,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                var results = new List<string>();
                var server = CreateServer();
                if (server == null) return (IReadOnlyList<string>)results;

                foreach (Database db in server.Databases)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (db.IsSystemObject) continue;
                    results.Add(db.Name);
                }

                return (IReadOnlyList<string>)results;
            }, cancellationToken);
        }
    }
}
