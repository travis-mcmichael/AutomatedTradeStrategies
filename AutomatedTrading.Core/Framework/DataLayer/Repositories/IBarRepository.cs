using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Framework.DataLayer.Repositories;

public interface IBarRepository
{
    /// <summary>Streams all bars in a date range, invoking the callback per row without loading everything into memory.</summary>
    Task StreamBarsAsync(DateTime start, DateTime end, Func<BarData, Task> onBar, CancellationToken ct = default);

    /// <summary>Returns all 1-minute bars belonging to a single session.</summary>
    Task<List<BarData>> GetBarsBySessionDateAsync(string sessionDate, CancellationToken ct = default);

    /// <summary>Batch-inserts bars, committing in chunks of <paramref name="batchSize"/>.</summary>
    Task<int> InsertBatchAsync(List<BarData> bars, int batchSize, CancellationToken ct = default);
}
