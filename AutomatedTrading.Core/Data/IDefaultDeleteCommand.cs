using System.Threading.Tasks;

namespace AutomatedTrading.Core.Data;

public interface IDefaultDeleteCommand<T> where T : IDatabaseEntity
{
    Task<int> Execute(T entity);
    Task<int> Execute(object where);
}