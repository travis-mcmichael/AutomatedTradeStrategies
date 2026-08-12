using System.Collections.Generic;
using System.Threading.Tasks;

namespace AutomatedTrading.Core.Data;

public interface IDefaultInsertCommand<T> where T : IDatabaseEntity
{
    Task<long> Execute(T entity);
    Task<IEnumerable<T>> Execute(IEnumerable<T> entities);
}