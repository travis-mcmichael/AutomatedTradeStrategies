using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Framework.DataLayer.Commands.Ticks;

public class GetAllTicksStreamQuery : IDbStreamQuery<TickData>
{
    private const string Sql =
        "SELECT TickTime, LastPrice, Bid, Ask, Volume, SessionDate, SessionTime FROM TickData ORDER BY TickTime ASC";

    public async Task ExecuteAsync(DbConnection connection, Func<TickData, Task> onItem, CancellationToken ct = default)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = Sql;

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            await onItem(new TickData
            {
                TickTime    = reader.GetInt64(0),
                LastPrice   = reader.GetFloat(1),
                Bid         = reader.IsDBNull(2) ? (float?)null : reader.GetFloat(2),
                Ask         = reader.IsDBNull(3) ? (float?)null : reader.GetFloat(3),
                Volume      = reader.GetInt32(4),
                SessionDate = reader.IsDBNull(5) ? null : reader.GetString(5),
                SessionTime = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }
    }
}
