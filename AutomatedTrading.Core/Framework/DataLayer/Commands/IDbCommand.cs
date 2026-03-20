using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace AutomatedTrading.Core.Framework.DataLayer.Commands;

/// <summary>Encapsulates a SQL INSERT / UPDATE / DELETE operation.</summary>
public interface IDbCommand<TResult>
{
    Task<TResult> ExecuteAsync(DbConnection connection, CancellationToken ct = default);
}
