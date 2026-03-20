using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Framework.DataLayer.Commands.Bars;

public class InsertBarBatchCommand : IDbCommand<int>
{
    // SQLite 3.24+ upsert syntax. ON CONFLICT targets the UNIQUE (Instrument, Timestamp, SessionDate)
    // constraint and updates all mutable columns in-place, preserving the existing Id.
    // For PostgreSQL the syntax is identical — just change the connection string.
    private const string Sql = @"
        INSERT INTO BarData
            (Instrument, Timeframe, Timestamp, SessionDate, Open, High, Low, Close, Volume)
        VALUES
            (@Instrument, @Timeframe, @Timestamp, @SessionDate, @Open, @High, @Low, @Close, @Volume)
        ON CONFLICT (Instrument, Timestamp, SessionDate) DO UPDATE SET
            Timeframe   = excluded.Timeframe,
            Open        = excluded.Open,
            High        = excluded.High,
            Low         = excluded.Low,
            Close       = excluded.Close,
            Volume      = excluded.Volume";

    private readonly List<BarData> bars;
    private readonly int batchSize;

    public InsertBarBatchCommand(List<BarData> bars, int batchSize)
    {
        this.bars      = bars;
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
                                     "@Open", "@High", "@Low", "@Close", "@Volume" })
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            cmd.Parameters.Add(p);
        }
    }

    private static void setParameters(DbCommand cmd, BarData bar)
    {
        cmd.Parameters["@Instrument"].Value  = bar.Instrument  ?? (object)DBNull.Value;
        cmd.Parameters["@Timeframe"].Value   = bar.Timeframe   ?? (object)DBNull.Value;
        cmd.Parameters["@Timestamp"].Value   = bar.Timestamp;
        cmd.Parameters["@SessionDate"].Value = bar.SessionDate ?? (object)DBNull.Value;
        cmd.Parameters["@Open"].Value        = safeDouble(bar.Open);
        cmd.Parameters["@High"].Value        = safeDouble(bar.High);
        cmd.Parameters["@Low"].Value         = safeDouble(bar.Low);
        cmd.Parameters["@Close"].Value       = safeDouble(bar.Close);
        cmd.Parameters["@Volume"].Value      = safeDouble(bar.Volume);
    }

    private static object safeDouble(double v) =>
        double.IsNaN(v) || double.IsInfinity(v) ? (object)DBNull.Value : v;
}
