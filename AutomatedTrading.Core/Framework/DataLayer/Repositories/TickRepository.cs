using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutomatedTrading.Core.Framework.DataLayer.Commands.Ticks;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Framework.DataLayer.Repositories;

public class TickRepository : ITickRepository
{
    private readonly IDbConnectionFactory connectionFactory;

    public TickRepository(IDbConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task StreamTicksAsync(Func<TickData, Task> onTick, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);
        await new GetAllTicksStreamQuery().ExecuteAsync(connection, onTick, ct);
    }

    public async Task<int> InsertBatchAsync(List<TickData> ticks, int batchSize, CancellationToken ct = default)
    {
        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);
        return await new InsertTickBatchCommand(ticks, batchSize).ExecuteAsync(connection, ct);
    }
}
