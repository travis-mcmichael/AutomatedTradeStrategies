using System.Collections.Generic;
using System.Threading.Tasks;

namespace AutomatedTrading.Core.Data;
public interface IDefaultRepository<T> where T : IDatabaseEntity
{
    Task<IEnumerable<T>> GetDataAsync(IDictionary<string, object> filter, string? orderby = null);
}