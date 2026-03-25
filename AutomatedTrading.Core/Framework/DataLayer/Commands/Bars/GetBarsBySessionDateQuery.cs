using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Framework.DataLayer.Commands.Bars;

public class GetBarsBySessionDateQuery : IDbQuery<List<BarData>>
{
    private const string Sql = @"
        SELECT Instrument, Timeframe, Timestamp, SessionDate, Open, High, Low, Close, Volume
        FROM BarData
        WHERE SessionDate = @SessionDate
        ORDER BY Timestamp ASC";

    private readonly string sessionDate;

    public GetBarsBySessionDateQuery(string sessionDate)
    {
        this.sessionDate = sessionDate;
    }

    public async Task<List<BarData>> ExecuteAsync(DbConnection connection, CancellationToken ct = default)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = Sql;
        addParameter(cmd, "@SessionDate", sessionDate);

        var results = new List<BarData>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(mapBar(reader));

        return results;
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
