using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutomatedTrading.Core.Framework.DataLayer.Commands.Bars;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Framework.DataLayer.Repositories;

public class BarRepository : IBarRepository
{
    private readonly IDbConnectionFactory connectionFactory;

    public BarRepository(IDbConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task StreamBarsAsync(DateTime start, DateTime end, Func<BarData, Task> onBar, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);
        await new GetBarsInRangeStreamQuery(start, end).ExecuteAsync(connection, onBar, ct);
    }

    public async Task<List<BarData>> GetBarsBySessionDateAsync(string sessionDate, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);
        return await new GetBarsBySessionDateQuery(sessionDate).ExecuteAsync(connection, ct);
    }

    public async Task<int> InsertBatchAsync(List<BarData> bars, int batchSize, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);
        return await new InsertBarBatchCommand(bars, batchSize).ExecuteAsync(connection, ct);
    }
}
