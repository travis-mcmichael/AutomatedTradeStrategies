using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Framework.DataLayer.Repositories;

public interface ITickRepository
{
    /// <summary>Streams all tick records, invoking the callback per row.</summary>
    Task StreamTicksAsync(Func<TickData, Task> onTick, CancellationToken ct = default);

    /// <summary>Batch-inserts ticks, committing in chunks of <paramref name="batchSize"/>.</summary>
    Task<int> InsertBatchAsync(List<TickData> ticks, int batchSize, CancellationToken ct = default);
}
