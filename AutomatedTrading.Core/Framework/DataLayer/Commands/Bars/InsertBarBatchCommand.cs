using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Framework.DataLayer.Commands.Bars;

public class InsertBarBatchCommand : IDbCommand<int>
{
    // INSERT OR REPLACE handles any UNIQUE constraint violation without requiring a specific
    // conflict target. If a row with the same (Instrument, Timestamp) already exists it is
    // deleted and re-inserted; the Id changes but that is acceptable for collected bar data.
    private const string Sql = @"
        INSERT OR REPLACE INTO BarData
            (Instrument, Timeframe, Timestamp, SessionDate, SessionTime, Open, High, Low, Close, Volume)
        VALUES
            (@Instrument, @Timeframe, @Timestamp, @SessionDate, @SessionTime, @Open, @High, @Low, @Close, @Volume)";

    private readonly List<BarData> bars;
    private readonly int batchSize;

    public InsertBarBatchCommand(List<BarData> bars, int batchSize)
    {
        this.bars = bars;
        this.batchSize = batchSize;
    }

    public async Task<int> ExecuteAsync(DbConnection connection, CancellationToken ct = default)
    {
        var total = 0;

        for (var i = 0; i < bars.Count; i += batchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batchEnd = Math.Min(i + batchSize, bars.Count);

            using var transaction = connection.BeginTransaction();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = Sql;
            cmd.Transaction = transaction;
            addParameters(cmd);

            for (var j = i; j < batchEnd; j++)
            {
                setParameters(cmd, bars[j]);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            transaction.Commit();
            total += batchEnd - i;
        }

        return total;
    }

    private static void addParameters(DbCommand cmd)
    {
        foreach (var name in new[] { "@Instrument", "@Timeframe", "@Timestamp", "@SessionDate",
            "@SessionTime", "@Open", "@High", "@Low", "@Close", "@Volume" })
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            cmd.Parameters.Add(p);
        }
    }

    private static void setParameters(DbCommand cmd, BarData bar)
    {
        cmd.Parameters["@Instrument"].Value = bar.Instrument ?? (object)DBNull.Value;
        cmd.Parameters["@Timeframe"].Value = bar.Timeframe ?? (object)DBNull.Value;
        cmd.Parameters["@Timestamp"].Value = new DateTimeOffset(bar.Timestamp.ToUniversalTime()).ToUnixTimeMilliseconds();
        cmd.Parameters["@SessionDate"].Value = bar.SessionDate ?? (object)DBNull.Value;
        cmd.Parameters["@SessionTime"].Value = bar.SessionTime ?? (object)DBNull.Value;
        cmd.Parameters["@Open"].Value = safeDouble(bar.Open);
        cmd.Parameters["@High"].Value = safeDouble(bar.High);
        cmd.Parameters["@Low"].Value = safeDouble(bar.Low);
        cmd.Parameters["@Close"].Value = safeDouble(bar.Close);
        cmd.Parameters["@Volume"].Value = safeDouble(bar.Volume);
    }

    private static object safeDouble(double v) =>
        double.IsNaN(v) || double.IsInfinity(v) ? (object)DBNull.Value : v;
}
