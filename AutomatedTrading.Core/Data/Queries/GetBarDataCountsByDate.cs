using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Data.Queries;

// Bar counts per session date for one calendar month: every trading day in the month (up to today for the
// current month, none for a future month), most recent first, with 0 for days that have no bars.
public class GetBarDataCountsByDate(IDbConnectionFactory connectionFactory)
{
    public async Task<IList<BarDataDateCount>> Execute(int year, int month)
    {
        var sessionDates = getTradingDaysInMonth(year, month);
        if (sessionDates.Count == 0) return [];
        var paramNames = sessionDates.Select((_, i) => $"@d{i}").ToList();

        using var connection = connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $@"
            SELECT SessionDate, Instrument, InstrumentVersion, COUNT(*) AS BarCount
            FROM BarData
            WHERE SessionDate IN ({string.Join(", ", paramNames)})
            GROUP BY SessionDate, Instrument, InstrumentVersion
            ORDER BY SessionDate, Instrument, InstrumentVersion";

        for (var i = 0; i < sessionDates.Count; i++)
            addParameter(cmd, paramNames[i], sessionDates[i].ToString(AppConstants.DATE_FORMAT));

        await connection.OpenAsync();

        var details = new Dictionary<string, List<BarDataInstrumentCount>>();
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var date = reader.GetString(0);
                if (!details.TryGetValue(date, out var list)) details[date] = list = [];
                list.Add(new BarDataInstrumentCount
                {
                    Instrument = reader.GetString(1),
                    InstrumentVersion = reader.GetString(2),
                    BarCount = reader.GetInt32(3)
                });
            }
        }

        return [.. sessionDates.Select(d =>
        {
            var key = d.ToString(AppConstants.DATE_FORMAT);
            var list = details.TryGetValue(key, out var l) ? l : [];
            return new BarDataDateCount { SessionDate = key, BarCount = list.Sum(x => x.BarCount), Details = list };
        })];
    }

    // Most-recent-first trading days (weekdays that aren't market holidays, via TradingCalendar) in the month,
    // stopping at today.
    private static List<DateTime> getTradingDaysInMonth(int year, int month)
    {
        var first = new DateTime(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        if (last > DateTime.Today) last = DateTime.Today;

        var dates = new List<DateTime>();
        for (var day = last; day >= first; day = day.AddDays(-1))
        {
            if (TradingCalendar.IsTradingDay(day))
                dates.Add(day);
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
