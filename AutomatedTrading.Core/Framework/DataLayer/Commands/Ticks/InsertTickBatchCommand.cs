using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Framework.DataLayer.Commands.Ticks;

public class InsertTickBatchCommand : IDbCommand<int>
{
    private const string Sql = @"
        INSERT INTO TickData
            (Instrument, TickTime, LastPrice, Volume, Bid, Ask, SessionDate, SessionTime)
        VALUES
            (@Instrument, @TickTime, @LastPrice, @Volume, @Bid, @Ask, @SessionDate, @SessionTime)
        ON CONFLICT (Instrument, TickTime) DO UPDATE SET
            LastPrice = excluded.LastPrice,
            Volume = excluded.Volume,
            Bid = excluded.Bid,
            Ask = excluded.Ask,
            SessionDate = excluded.SessionDate,
            SessionTime = excluded.SessionTime";

    private readonly List<TickData> ticks;
    private readonly int batchSize;

    public InsertTickBatchCommand(List<TickData> ticks, int batchSize)
    {
        this.ticks     = ticks;
        this.batchSize = batchSize;
    }

    public async Task<int> ExecuteAsync(DbConnection connection, CancellationToken ct = default)
    {
        var total = 0;

        for (var i = 0; i < ticks.Count; i += batchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batchEnd = Math.Min(i + batchSize, ticks.Count);

            using var transaction = connection.BeginTransaction();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = Sql;
            cmd.Transaction = transaction;
            addParameters(cmd);

            for (var j = i; j < batchEnd; j++)
            {
                setParameters(cmd, ticks[j]);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            transaction.Commit();
            total += batchEnd - i;
        }

        return total;
    }

    private static void addParameters(DbCommand cmd)
    {
        foreach (var name in new[] { "@Instrument", "@TickTime", "@LastPrice", "@Volume", "@Bid", "@Ask", "@SessionDate", "@SessionTime" })
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            cmd.Parameters.Add(p);
        }
    }

    private static void setParameters(DbCommand cmd, TickData tick)
    {
        cmd.Parameters["@Instrument"].Value = tick.Instrument ?? (object)DBNull.Value;
        cmd.Parameters["@TickTime"].Value = tick.TickTime;
        cmd.Parameters["@LastPrice"].Value = tick.LastPrice;
        cmd.Parameters["@Volume"].Value = tick.Volume;
        cmd.Parameters["@Bid"].Value = tick.Bid.HasValue ? (object)tick.Bid.Value : DBNull.Value;
        cmd.Parameters["@Ask"].Value = tick.Ask.HasValue ? (object)tick.Ask.Value : DBNull.Value;
        cmd.Parameters["@SessionDate"].Value = tick.SessionDate ?? (object)DBNull.Value;
        cmd.Parameters["@SessionTime"].Value = tick.SessionTime ?? (object)DBNull.Value;
    }
}
