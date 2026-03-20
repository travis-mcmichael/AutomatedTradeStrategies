using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace AutomatedTrading.Core.Framework.DataLayer.Commands;

/// <summary>
/// Encapsulates a SQL SELECT that streams results one row at a time via a callback,
/// avoiding loading the full result set into memory.
/// Compatible with both .NET Framework and .NET 8+.
/// </summary>
public interface IDbStreamQuery<T>
{
    Task ExecuteAsync(DbConnection connection, Func<T, Task> onItem, CancellationToken ct = default);
}
