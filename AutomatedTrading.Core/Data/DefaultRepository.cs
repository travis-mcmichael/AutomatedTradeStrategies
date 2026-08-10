
using AutomatedTrading.Core.Utility;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutomatedTrading.Core.Data;

public class DefaultRepository<T>(string connectionString, ILogger<DefaultRepository<T>> logger) : IDefaultRepository<T> where T : IDatabaseEntity, new()
{
    public async Task<IEnumerable<T>> GetDataAsync(IDictionary<string, object> filters, string? orderby = null)
    {
        var entity = new T();
        var tableName = entity.TableName;

        if (filters == null || filters.Count == 0)
        {
            logger.LogWarning($"No filter criteria added for {tableName} query.");
            return [];
        }

        logger.LogInformation($"DefaultRepository.GetDataAsync() for table {tableName}");

        var entityProps = typeof(T).GetProperties()
            .Where(p => p.Name != "TableName" && SqlHelper.IsSimpleType(p.PropertyType));
        var selectList = string.Join(", ", entityProps.Select(p =>
        {
            var col = SqlHelper.GetColumnName(p);
            return col == p.Name ? col : $"{col} AS \"{p.Name}\"";
        }));

        var where = new List<string>();
        var sql = new StringBuilder($" SELECT {selectList} FROM {tableName} ");
        var parameters = new DynamicParameters();

        foreach (var filter in filters)
        {
            if (filter.Value is string s && (s.StartsWith('%') || s.EndsWith('%')))
            {
                where.Add($" {filter.Key} ILIKE @{filter.Key} ");
            }
            else
            {
                where.Add($" {filter.Key} = @{filter.Key} ");
            }
            parameters.Add(filter.Key, filter.Value);
        }

        sql.Append($" WHERE {string.Join(" AND ", where)}");
        if (orderby is not null or "") sql.Append($" ORDER BY {orderby} ");
        var final = sql.ToString();
        logger.LogInformation("Executing SQL: {@final}", final);
        logger.LogInformation("Parameters: {@parameters}", parameters);

        using var connection = new NpgsqlConnection(connectionString);
        return await connection.QueryAsync<T>(final, parameters);
    }

    public Task<IEnumerable<T>> GetDataAsync(IDictionary<string, object> filter, string? orderby = null)
    {
        throw new System.NotImplementedException();
    }
}