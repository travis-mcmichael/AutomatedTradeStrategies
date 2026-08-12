using System.Threading.Tasks;

namespace AutomatedTrading.Core.Data;

public interface IDefaultUpdateCommand<T> where T : IDatabaseEntity
{
    Task<int> UpdateAsync(T entity);
    Task<int> UpdateAsync(object values, object where);
}