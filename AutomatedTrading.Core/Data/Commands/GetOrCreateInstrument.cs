using System;
using System.Threading.Tasks;
using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Utility;

namespace AutomatedTrading.Core.Data.Commands;

// Returns the Id of the Instrument with the given name, inserting it first if it doesn't exist.
public class GetOrCreateInstrument(IDbConnectionFactory connectionFactory)
{
    public async Task<long> Execute(string instrumentName)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        using (var select = connection.CreateCommand())
        {
            select.CommandText = "SELECT Id FROM Instrument WHERE InstrumentName = @InstrumentName";
            SqlHelper.AddParameter(select, "@InstrumentName", instrumentName);
            var existing = await select.ExecuteScalarAsync();
            if (existing != null && existing != DBNull.Value)
                return Convert.ToInt64(existing);
        }

        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO Instrument (InstrumentName) VALUES (@InstrumentName) RETURNING Id";
        SqlHelper.AddParameter(insert, "@InstrumentName", instrumentName);
        return Convert.ToInt64(await insert.ExecuteScalarAsync());
    }
}
