using System.Collections.Generic;
using System.Threading.Tasks;

namespace AutomatedTrading.Core.Data;

public interface IDefaultInsertCommand<T> where T : IDatabaseEntity
{
    Task<long> InsertAsync(T entity);
    Task<IEnumerable<T>> InsertAsync(IEnumerable<T> entities);
}