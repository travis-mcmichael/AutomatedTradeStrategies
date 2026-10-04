using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Reflection;
using AutomatedTrading.Core.Data;

namespace AutomatedTrading.Core.Utility;
public class SqlHelper
{
    public static string GetColumnName(PropertyInfo prop)
    {
        var attr = prop.GetCustomAttribute<ColumnNameAttribute>();
        return attr?.Name ?? prop.Name;
    }

    public static bool IsSimpleType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type.IsPrimitive)
            return true;

        if (type.IsEnum)
            return true;

        return type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(Guid);
    }

    // SQLite has no notion of schemas the way SQL Server's "dbo" does. Every IDatabaseEntity
    // in this codebase sets SchemaName to "dbo" (ported convention), so that value is always
    // treated as "no schema prefix" here.
    public static string GetTableName(IDatabaseEntity entity) =>
        entity.SchemaName is null or "dbo" ? entity.TableName : $"{entity.SchemaName}.{entity.TableName}";

    public static void AddParameter(DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = ToDbValue(value);
        cmd.Parameters.Add(p);
    }

    // DateTime columns in this schema (e.g. BarData.Timestamp) are stored as Unix epoch
    // milliseconds, matching InsertBarBatchCommand/GetBarsBySessionDateQuery — not SQLite's
    // own datetime string handling.
    public static object ToDbValue(object? value) => value switch
    {
        null => DBNull.Value,
        DateTime dt => new DateTimeOffset(dt.ToUniversalTime()).ToUnixTimeMilliseconds(),
        _ => value
    };

    // Expects the reader's columns to be aliased to the property name where it differs from
    // the underlying DB column (see DefaultRepository's SELECT list), so lookup is by prop.Name.
    public static T MapEntity<T>(DbDataReader reader, IEnumerable<PropertyInfo> props) where T : new()
    {
        var entity = new T();
        foreach (var prop in props)
        {
            var ordinal = reader.GetOrdinal(prop.Name);
            if (reader.IsDBNull(ordinal)) continue;

            var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            object value = targetType == typeof(DateTime)
                ? DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(ordinal)).LocalDateTime
                : Convert.ChangeType(reader.GetValue(ordinal), targetType);

            prop.SetValue(entity, value);
        }
        return entity;
    }
}