using System;
using System.Reflection;

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
}