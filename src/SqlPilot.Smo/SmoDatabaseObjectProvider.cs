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

        /// <summary>
        /// The server behind an Object Explorer connection, or null when there is none.
        /// </summary>
        public static Server CreateServer(ConnectionDescriptor connection)
            => connection == null ? null : new Server(CreateServerConnection(connection));

        /// <summary>
        /// The connection SQL Pilot opens for itself, for indexing and for scripting. It
        /// asks for the encryption and the certificate trust that the Object Explorer
        /// connection in <paramref name="connection"/> asked for, no more and no less.
        /// </summary>
        public static ServerConnection CreateServerConnection(ConnectionDescriptor connection)
        {
            // Object Explorer's connection is of our own SMO (SSMS 18, 22): its string was
            // written for our SqlClient and already says everything the Connect dialog did.
            if (connection.RawConnectionInfo is SqlConnectionInfo
                && !string.IsNullOrEmpty(connection.ConnectionString))
            {
                return new ServerConnection { ConnectionString = connection.ConnectionString };
            }

            // Otherwise it is of another SMO (SSMS 20: 17 against our 16) and its string is for
            // a SqlClient we don't have; SMO 16 rejects "Multiple Active Result Sets" for one.
            // Carry over what both drivers know. See "SSMS 20 loads two SMO versions" in the notes.
            var serverConn = new ServerConnection();
            serverConn.ServerInstance = connection.ServerName;
            // SMO 16 cannot open an encrypted connection to LocalDB, which never leaves the machine.
            serverConn.EncryptConnection = connection.RequestsEncryption
                && !ConnectionDescriptor.IsLocalDbServerName(connection.ServerName);
            serverConn.TrustServerCertificate = connection.TrustsServerCertificate;

            if (!connection.UseIntegratedSecurity)
            {
                serverConn.LoginSecure = false;
                serverConn.Login = connection.UserName ?? "";
                serverConn.Password = connection.Password ?? "";
            }

            return serverConn;
        }

        public Task<IReadOnlyList<DatabaseObject>> GetObjectsAsync(
            string serverName,
            string databaseName,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                var results = new List<DatabaseObject>();
                var server = CreateServer(_connection);
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
                var server = CreateServer(_connection);
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
