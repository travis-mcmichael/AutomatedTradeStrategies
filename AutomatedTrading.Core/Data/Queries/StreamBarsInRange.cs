using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Models;
using Microsoft.Extensions.Logging;

namespace AutomatedTrading.Core.Data.Queries;

public class StreamBarsInRange(IDbConnectionFactory connectionFactory, ILogger<StreamBarsInRange> logger)
{
    private const string Sql = @"
        SELECT Instrument, Timeframe, Timestamp, SessionDate, SessionTime, Open, High, Low, Close, Volume
        FROM BarData
        WHERE Timestamp >= @StartTimestamp AND Timestamp <= @EndTimestamp
        ORDER BY Timestamp ASC";

    public async Task ExecuteAsync(DateTime start, DateTime end, Func<BarData, Task> onItem, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = Sql;
        addParameter(cmd, "@StartTimestamp", new DateTimeOffset(start, TimeSpan.Zero).ToUnixTimeMilliseconds());
        addParameter(cmd, "@EndTimestamp", new DateTimeOffset(end, TimeSpan.Zero).ToUnixTimeMilliseconds());

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            await onItem(mapBar(reader));
        }
    }

    private static void addParameter(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static BarData mapBar(DbDataReader r) => new BarData
    {
        Instrument = r.GetString(0),
        Timeframe = r.GetString(1),
        Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(2)).UtcDateTime,
        SessionDate = r.IsDBNull(3) ? null : r.GetString(3),
        SessionTime = r.IsDBNull(4) ? null : r.GetString(4),
        Open = r.GetDouble(5),
        High = r.GetDouble(6),
        Low = r.GetDouble(7),
        Close = r.GetDouble(8),
        Volume = r.GetDouble(9)
    };
}
