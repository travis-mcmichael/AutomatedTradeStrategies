using System.Linq;
using System.Threading.Tasks;
using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Utility;
using Microsoft.Extensions.Logging;

namespace AutomatedTrading.Core.Data;

public class DefaultUpdateCommand<T>(IDbConnectionFactory connectionFactory, ILogger<DefaultUpdateCommand<T>> logger) : IDefaultUpdateCommand<T> where T : IDatabaseEntity, new()
{
    public async Task<int> Execute(T entity)
    {
        var props = typeof(T).GetProperties().Where(p =>
            p.Name != "Id" &&
            p.Name != "TableName" &&
            p.Name != "SchemaName" &&
            SqlHelper.IsSimpleType(p.PropertyType)).ToList();
        var setClause = string.Join(", ", props.Select(p => $"{SqlHelper.GetColumnName(p)}=@{p.Name}"));
        var table = SqlHelper.GetTableName(entity);

        var sql = $"UPDATE {table} SET {setClause} WHERE Id=@Id";

        logger.LogInformation($"UPDATE for {table} sql : {sql}");

        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var prop in props)
            SqlHelper.AddParameter(cmd, $"@{prop.Name}", prop.GetValue(entity));
        SqlHelper.AddParameter(cmd, "@Id", entity.Id);

        return await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> Execute(object values, object where)
    {
        var entity = new T();

        var setProps = values.GetType().GetProperties();
        var whereProps = where.GetType().GetProperties();

        if (setProps.Length is 0)
        {
            logger.LogWarning($"UPDATE for {entity.TableName} exited: SET or WHERE empty");
            return 0;
        }

        var setClause = string.Join(", ", setProps.Select(p => $"{p.Name} = @{p.Name}"));
        var whereClause = whereProps.Length > 0 ?
            " WHERE " + string.Join(" AND ", whereProps.Select(p => $"{p.Name} = @where_{p.Name}")) : "";
        var table = SqlHelper.GetTableName(entity);

        var sql = $"UPDATE {table} SET {setClause} {whereClause}";

        logger.LogInformation($"UPDATE for {table} sql: {sql}");

        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var prop in setProps)
            SqlHelper.AddParameter(cmd, $"@{prop.Name}", prop.GetValue(values));
        foreach (var prop in whereProps)
            SqlHelper.AddParameter(cmd, $"@where_{prop.Name}", prop.GetValue(where));

        return await cmd.ExecuteNonQueryAsync();
    }
}
