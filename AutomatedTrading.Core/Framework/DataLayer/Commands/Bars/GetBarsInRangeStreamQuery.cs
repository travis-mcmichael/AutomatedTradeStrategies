using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Framework.DataLayer.Commands.Bars;

public class GetBarsInRangeStreamQuery : IDbStreamQuery<BarData>
{
    private const string Sql = @"
        SELECT Instrument, Timeframe, Timestamp, SessionDate, Open, High, Low, Close, Volume
        FROM BarData
        WHERE Timestamp >= @StartTimestamp AND Timestamp <= @EndTimestamp
        ORDER BY Timestamp ASC";

    private readonly DateTime start;
    private readonly DateTime end;

    public GetBarsInRangeStreamQuery(DateTime start, DateTime end)
    {
        this.start = start;
        this.end   = end;
    }

    public async Task ExecuteAsync(DbConnection connection, Func<BarData, Task> onItem, CancellationToken ct = default)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = Sql;
        addParameter(cmd, "@StartTimestamp", new DateTimeOffset(start, TimeSpan.Zero).ToUnixTimeMilliseconds());
        addParameter(cmd, "@EndTimestamp",   new DateTimeOffset(end,   TimeSpan.Zero).ToUnixTimeMilliseconds());

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
        Instrument  = r.GetString(0),
        Timeframe   = r.GetString(1),
        Timestamp   = DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(2)).UtcDateTime,
        SessionDate = r.IsDBNull(3) ? null : r.GetString(3),
        Open        = r.GetDouble(4),
        High        = r.GetDouble(5),
        Low         = r.GetDouble(6),
        Close       = r.GetDouble(7),
        Volume      = r.GetDouble(8)
    };
}
