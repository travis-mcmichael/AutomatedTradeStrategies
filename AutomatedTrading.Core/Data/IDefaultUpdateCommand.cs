using System.Threading.Tasks;

namespace AutomatedTrading.Core.Data;

public interface IDefaultUpdateCommand<T> where T : IDatabaseEntity
{
    Task<int> Execute(T entity);
    Task<int> Execute(object values, object where);
}