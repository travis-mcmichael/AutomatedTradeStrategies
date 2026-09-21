using System;
using System.Collections.Generic;
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
// and uses INSERT OR REPLACE so re-importing a row at an already-stored (Instrument, Timestamp)
// overwrites the existing row instead of violating BarData's UNIQUE (Instrument, Timestamp) constraint.
public class BarDataInsertCommand(IDbConnectionFactory connectionFactory, ILogger<BarDataInsertCommand> logger) : IDefaultInsertCommand<BarData>
{
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
        var sql = buildInsertSql(tableName, props);

        using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        foreach (var batch in chunk(list, AppConstants.BAR_DATA_BATCH_SIZE))
        {
            logger.LogInformation($"INSERT OR REPLACE batch of {batch.Count} for {tableName}");

            using var transaction = connection.BeginTransaction();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Transaction = transaction;
            foreach (var prop in props)
                SqlHelper.AddParameter(cmd, $"@{prop.Name}", null);

            foreach (var entity in batch)
            {
                foreach (var prop in props)
                    cmd.Parameters[$"@{prop.Name}"].Value = prop.GetValue(entity) ?? DBNull.Value;
                entity.Id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            }

            transaction.Commit();
        }

        return list;
    }

    private static string buildInsertSql(string tableName, List<PropertyInfo> props)
    {
        var columns = string.Join(", ", props.Select(SqlHelper.GetColumnName));
        var paramNames = string.Join(", ", props.Select(p => $"@{p.Name}"));
        return $"INSERT OR REPLACE INTO {tableName} ({columns}) VALUES ({paramNames}) RETURNING Id";
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
