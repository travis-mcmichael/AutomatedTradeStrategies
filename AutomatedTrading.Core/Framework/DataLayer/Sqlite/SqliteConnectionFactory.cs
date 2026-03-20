using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace AutomatedTrading.Core.Framework.DataLayer.Sqlite;

// The only class in the data layer that references Microsoft.Data.Sqlite directly.
// Swap this out (e.g. NpgsqlConnectionFactory) to change the underlying database.
public class SqliteConnectionFactory : IDbConnectionFactory
{
    private readonly string connectionString;

    public SqliteConnectionFactory(string connectionString)
    {
        this.connectionString = connectionString;
    }

    public DbConnection CreateConnection() => new SqliteConnection(connectionString);
}
