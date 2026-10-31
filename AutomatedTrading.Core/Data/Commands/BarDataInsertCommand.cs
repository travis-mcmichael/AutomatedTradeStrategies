using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Utility;
using Microsoft.Extensions.Logging;

namespace AutomatedTrading.Core.Data.Commands;

// Mirrors DefaultInsertCommand<T>, but commits in batches of AppConstants.BAR_DATA_BATCH_SIZE
// and upserts on (SessionDate, SessionTime, Instrument, InstrumentVersion): an existing row with
// the same key is updated in place with the incoming values, otherwise a new row is inserted.
// The key isn't backed by a UNIQUE constraint, so this is an UPDATE-then-INSERT rather than ON CONFLICT.
public class BarDataInsertCommand(IDbConnectionFactory connectionFactory, ILogger<BarDataInsertCommand> logger) : IDefaultInsertCommand<BarData>
{
    private static readonly string[] keyPropNames =
        [nameof(BarData.SessionDate), nameof(BarData.SessionTime), nameof(BarData.Instrument), nameof(BarData.InstrumentVersion)];

    public async Task<long> Execute(BarData entity)
    {
        var inserted = await Execute([entity]);
        return inserted.Single().Id;
    }

    public async Task<IEnumerable<BarData>> Execute(IEnumerable<BarData> entities)
    {
        var list = entities.ToList();
        if (list.Count == 0) return list;

        var tableName = SqlHelper.GetTableName(list[0]);
        var props = insertableProps();
        var updateSql = buildUpdateSql(tableName, props);
        var insertSql = buildInsertSql(tableName, props);

        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        foreach (var batch in chunk(list, AppConstants.BAR_DATA_BATCH_SIZE))
        {
            logger.LogInformation($"UPSERT batch of {batch.Count} for {tableName}");

            using var transaction = connection.BeginTransaction();
            using var updateCmd = createCommand(connection, transaction, updateSql, props);
            using var insertCmd = createCommand(connection, transaction, insertSql, props);

            foreach (var entity in batch)
            {
                bindParameters(updateCmd, props, entity);
                var existingId = await updateCmd.ExecuteScalarAsync();
                if (existingId is not null && existingId is not DBNull)
                {
                    entity.Id = Convert.ToInt64(existingId);
                    continue;
                }

                bindParameters(insertCmd, props, entity);
                entity.Id = Convert.ToInt64(await insertCmd.ExecuteScalarAsync());
            }

            transaction.Commit();
        }

        return list;
    }

    private static DbCommand createCommand(DbConnection connection, DbTransaction transaction, string sql, List<PropertyInfo> props)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = transaction;
        foreach (var prop in props)
            SqlHelper.AddParameter(cmd, $"@{prop.Name}", null);
        return cmd;
    }

    private static void bindParameters(DbCommand cmd, List<PropertyInfo> props, BarData entity)
    {
        foreach (var prop in props)
            cmd.Parameters[$"@{prop.Name}"].Value = SqlHelper.ToDbValue(prop.GetValue(entity));
    }

    // IS (rather than =) so a NULL SessionDate/SessionTime still matches an existing NULL.
    private static string buildUpdateSql(string tableName, List<PropertyInfo> props)
    {
        var setClause = string.Join(", ", props
            .Where(p => !keyPropNames.Contains(p.Name))
            .Select(p => $"{SqlHelper.GetColumnName(p)} = @{p.Name}"));
        var whereClause = string.Join(" AND ", props
            .Where(p => keyPropNames.Contains(p.Name))
            .Select(p => $"{SqlHelper.GetColumnName(p)} IS @{p.Name}"));
        return $"UPDATE {tableName} SET {setClause} WHERE {whereClause} RETURNING Id";
    }

    private static string buildInsertSql(string tableName, List<PropertyInfo> props)
    {
        var columns = string.Join(", ", props.Select(SqlHelper.GetColumnName));
        var paramNames = string.Join(", ", props.Select(p => $"@{p.Name}"));
        return $"INSERT INTO {tableName} ({columns}) VALUES ({paramNames}) RETURNING Id";
    }

    private static List<PropertyInfo> insertableProps() =>
        typeof(BarData).GetProperties()
            .Where(p => p.Name != "Id" && p.Name != "TableName" && p.Name != "SchemaName" && SqlHelper.IsSimpleType(p.PropertyType))
            .ToList();

    private static IEnumerable<List<BarData>> chunk(List<BarData> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
            yield return source.GetRange(i, Math.Min(size, source.Count - i));
    }
}
