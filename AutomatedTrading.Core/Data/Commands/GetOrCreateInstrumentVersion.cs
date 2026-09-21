using System;
using System.Threading.Tasks;
using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Utility;

namespace AutomatedTrading.Core.Data.Commands;

// Returns the Id of the InstrumentVersion for the given instrument and version (e.g. "03-26"),
// inserting it first if it doesn't exist.
public class GetOrCreateInstrumentVersion(IDbConnectionFactory connectionFactory)
{
    public async Task<long> Execute(long instrumentId, string version)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        using (var select = connection.CreateCommand())
        {
            select.CommandText = "SELECT Id FROM InstrumentVersion WHERE InstrumentId = @InstrumentId AND Version = @Version";
            SqlHelper.AddParameter(select, "@InstrumentId", instrumentId);
            SqlHelper.AddParameter(select, "@Version", version);
            var existing = await select.ExecuteScalarAsync();
            if (existing != null && existing != DBNull.Value)
                return Convert.ToInt64(existing);
        }

        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO InstrumentVersion (InstrumentId, Version) VALUES (@InstrumentId, @Version) RETURNING Id";
        SqlHelper.AddParameter(insert, "@InstrumentId", instrumentId);
        SqlHelper.AddParameter(insert, "@Version", version);
        return Convert.ToInt64(await insert.ExecuteScalarAsync());
    }
}
