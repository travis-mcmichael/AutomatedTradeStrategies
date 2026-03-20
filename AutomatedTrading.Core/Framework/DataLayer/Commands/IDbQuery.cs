using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace AutomatedTrading.Core.Framework.DataLayer.Commands;

/// <summary>Encapsulates a SQL SELECT that returns a single materialized result.</summary>
public interface IDbQuery<TResult>
{
    Task<TResult> ExecuteAsync(DbConnection connection, CancellationToken ct = default);
}
