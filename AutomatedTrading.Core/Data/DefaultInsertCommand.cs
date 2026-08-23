using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Utility;
using Microsoft.Extensions.Logging;

namespace AutomatedTrading.Core.Data;

public class DefaultInsertCommand<T>(IDbConnectionFactory connectionFactory, ILogger<DefaultInsertCommand<T>> logger) : IDefaultInsertCommand<T> where T : IDatabaseEntity, new()
{
    public async Task<long> Execute(T entity)
    {
        var tableName = SqlHelper.GetTableName(entity);
        var props = insertableProps();
        var sql = buildInsertSql(tableName, props);

        logger.LogInformation($"INSERT for {tableName} sql: {sql}");

        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var prop in props)
            SqlHelper.AddParameter(cmd, $"@{prop.Name}", prop.GetValue(entity));

        var id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        entity.Id = id;
        return id;
    }

    public async Task<IEnumerable<T>> Execute(IEnumerable<T> entities)
    {
        var list = entities.ToList();
        if (list.Count == 0) return list;

        var tableName = SqlHelper.GetTableName(list[0]);
        var props = insertableProps();
        var sql = buildInsertSql(tableName, props);

        logger.LogInformation($"INSERT batch for {tableName} sql: {sql}");

        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = transaction;
        foreach (var prop in props)
            SqlHelper.AddParameter(cmd, $"@{prop.Name}", null);

        foreach (var entity in list)
        {
            foreach (var prop in props)
                cmd.Parameters[$"@{prop.Name}"].Value = prop.GetValue(entity) ?? DBNull.Value;
            entity.Id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        transaction.Commit();
        return list;
    }

    private static string buildInsertSql(string tableName, List<PropertyInfo> props)
    {
        var columns = string.Join(", ", props.Select(SqlHelper.GetColumnName));
        var paramNames = string.Join(", ", props.Select(p => $"@{p.Name}"));
        return $"INSERT INTO {tableName} ({columns}) VALUES ({paramNames}) RETURNING Id";
    }

    private static List<PropertyInfo> insertableProps() =>
        typeof(T).GetProperties()
            .Where(p => p.Name != "Id" && p.Name != "TableName" && p.Name != "SchemaName" && SqlHelper.IsSimpleType(p.PropertyType))
            .ToList();
}
