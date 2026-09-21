using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Data.Queries;

public class GetBarDataCountsByDate(IDbConnectionFactory connectionFactory)
{
    private const int TRADING_DAYS = 30;

    public async Task<IList<BarDataDateCount>> Execute()
    {
        var sessionDates = getRecentTradingDays(TRADING_DAYS);
        var paramNames = sessionDates.Select((_, i) => $"@d{i}").ToList();

        using var connection = connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $@"
            SELECT SessionDate, COUNT(*) AS BarCount
            FROM BarData
            WHERE SessionDate IN ({string.Join(", ", paramNames)})
            GROUP BY SessionDate";

        for (var i = 0; i < sessionDates.Count; i++)
            addParameter(cmd, paramNames[i], sessionDates[i].ToString(AppConstants.DATE_FORMAT));

        await connection.OpenAsync();

        var counts = new Dictionary<string, int>();
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                counts[reader.GetString(0)] = reader.GetInt32(1);
        }

        return [.. sessionDates.Select(d =>
        {
            var key = d.ToString(AppConstants.DATE_FORMAT);
            return new BarDataDateCount { SessionDate = key, BarCount = counts.TryGetValue(key, out var count) ? count : 0 };
        })];
    }

    // Most-recent-first: today (if a trading day) walking back through the last
    // TRADING_DAYS sessions, skipping weekends/holidays via TradingCalendar.
    private static List<DateTime> getRecentTradingDays(int count)
    {
        var cursor = DateTime.Today;
        if (!TradingCalendar.IsTradingDay(cursor))
            cursor = TradingCalendar.GetPreviousSessionDate(cursor);

        var dates = new List<DateTime>(count);
        for (var i = 0; i < count; i++)
        {
            dates.Add(cursor);
            cursor = TradingCalendar.GetPreviousSessionDate(cursor);
        }
        return dates;
    }

    private static void addParameter(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
