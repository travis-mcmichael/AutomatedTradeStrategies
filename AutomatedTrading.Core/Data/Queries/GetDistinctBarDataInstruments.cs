using System.Collections.Generic;
using System.Threading.Tasks;
using AutomatedTrading.Core.Framework.DataLayer;

namespace AutomatedTrading.Core.Data.Queries;

public class GetDistinctBarDataInstruments(IDbConnectionFactory connectionFactory)
{
    public async Task<IList<string>> Execute()
    {
        using var connection = connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT Instrument FROM BarData WHERE Instrument IS NOT NULL ORDER BY Instrument";

        await connection.OpenAsync();

        var instruments = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            instruments.Add(reader.GetString(0));

        return instruments;
    }
}
