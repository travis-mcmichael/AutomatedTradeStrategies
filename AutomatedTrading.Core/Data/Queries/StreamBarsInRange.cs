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
        SELECT 
            Timeframe, 
            Timestamp, 
            SessionDate, 
            SessionTime, 
            Open, 
            High, 
            Low, 
            Close, 
            Volume,
            Instrument,
            InstrumentVersion
        FROM BarData b
        WHERE Instrument = @Instrument
          AND Timestamp >= @StartTimestamp AND Timestamp <= @EndTimestamp
        ORDER BY Timestamp ASC";

    public async Task ExecuteAsync(string instrument, DateTime start, DateTime end, Func<BarData, Task> onItem, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = Sql;
        addParameter(cmd, "@Instrument", instrument);
        addParameter(cmd, "@StartTimestamp", new DateTimeOffset(start, TimeSpan.Zero).ToUnixTimeMilliseconds());
        addParameter(cmd, "@EndTimestamp", new DateTimeOffset(end, TimeSpan.Zero).ToUnixTimeMilliseconds());

        await connection.OpenAsync(ct);
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
        Timeframe = r.GetString(0),
        Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(1)).UtcDateTime,
        SessionDate = r.IsDBNull(2) ? null : r.GetString(2),
        SessionTime = r.IsDBNull(3) ? null : r.GetString(3),
        Open = r.GetDouble(4),
        High = r.GetDouble(5),
        Low = r.GetDouble(6),
        Close = r.GetDouble(7),
        Volume = r.GetDouble(8),
        Instrument = r.GetString(9),
        InstrumentVersion = r.GetString(10),
    };
}
