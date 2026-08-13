
using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Utility;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutomatedTrading.Core.Data;

public class DefaultRepository<T>(IDbConnectionFactory connectionFactory, ILogger<DefaultRepository<T>> logger) : IDefaultRepository<T> where T : IDatabaseEntity, new()
{
    public async Task<IEnumerable<T>> GetDataAsync(IDictionary<string, object> filters, string? orderby = null)
    {
        var entity = new T();
        var tableName = SqlHelper.GetTableName(entity);

        if (filters == null || filters.Count == 0)
        {
            logger?.LogWarning($"No filter criteria added for {tableName} query.");
            return [];
        }

        logger?.LogInformation($"DefaultRepository.GetDataAsync() for table {tableName}");

        var entityProps = typeof(T).GetProperties()
            .Where(p => p.Name != "TableName" && p.Name != "SchemaName" && SqlHelper.IsSimpleType(p.PropertyType))
            .ToList();
        var selectList = string.Join(", ", entityProps.Select(p =>
        {
            var col = SqlHelper.GetColumnName(p);
            return col == p.Name ? col : $"{col} AS \"{p.Name}\"";
        }));

        using var connection = connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();

        var where = new List<string>();
        foreach (var filter in filters)
        {
            var paramName = $"@{filter.Key}";
            // SQLite's LIKE is case-insensitive for ASCII by default, matching Postgres' ILIKE closely enough here.
            where.Add(filter.Value is string s && (s.StartsWith("%") || s.EndsWith("%"))
                ? $" {filter.Key} LIKE {paramName} "
                : $" {filter.Key} = {paramName} ");
            SqlHelper.AddParameter(cmd, paramName, filter.Value);
        }

        var sql = new StringBuilder($" SELECT {selectList} FROM {tableName} ");
        sql.Append($" WHERE {string.Join(" AND ", where)}");
        if (orderby is not null or "") sql.Append($" ORDER BY {orderby} ");
        var final = sql.ToString();
        cmd.CommandText = final;

        logger?.LogInformation("Executing SQL: {@final}", final);
        logger?.LogInformation("Parameters: {@parameters}", filters);

        await connection.OpenAsync();

        var results = new List<T>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(SqlHelper.MapEntity<T>(reader, entityProps));

        return results;
    }
}