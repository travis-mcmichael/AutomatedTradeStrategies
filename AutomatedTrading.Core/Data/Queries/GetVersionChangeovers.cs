using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Utility;

namespace AutomatedTrading.Core.Data.Queries;

// Finds contract rolls within a date range. For each pair of consecutive versions (ordered by their
// "MM-yy" expiry), the changeover is the first session date on which the newer version's total volume
// exceeds the older version's. The adjustment is the difference between the two versions' first Open
// on that date.
public class GetVersionChangeovers(IDbConnectionFactory connectionFactory)
{
    private const string VERSION_FORMAT = "MM-yy";

    private class DailyVersion
    {
        public string SessionDate = default!;
        public string Version = default!;
        public double Volume;
        public double FirstOpen;
    }

    public async Task<IList<VersionChangeover>> Execute(string instrument, DateTime start, DateTime end)
    {
        var daily = await loadDailyVersions(instrument, start, end);

        var versions = daily
            .Select(d => d.Version)
            .Distinct()
            .Select(v => (Version: v, Expiry: parseVersion(v)))
            .OrderBy(v => v.Expiry)
            .Select(v => v.Version)
            .ToList();

        var byVersion = daily
            .GroupBy(d => d.Version)
            .ToDictionary(g => g.Key, g => g.ToDictionary(d => d.SessionDate));

        var changeovers = new List<VersionChangeover>();
        for (var i = 1; i < versions.Count; i++)
        {
            var oldDays = byVersion[versions[i - 1]];
            var newDays = byVersion[versions[i]];

            // Session dates are yyyyMMdd, so ordinal ordering is chronological.
            var changeoverDate = oldDays.Keys
                .Where(newDays.ContainsKey)
                .OrderBy(d => d, StringComparer.Ordinal)
                .FirstOrDefault(d => newDays[d].Volume > oldDays[d].Volume);

            if (changeoverDate == null) continue;

            changeovers.Add(new VersionChangeover
            {
                SessionDate = changeoverDate,
                OldVersion = versions[i - 1],
                NewVersion = versions[i],
                Adjustment = newDays[changeoverDate].FirstOpen - oldDays[changeoverDate].FirstOpen
            });
        }

        return changeovers;
    }

    private async Task<List<DailyVersion>> loadDailyVersions(string instrument, DateTime start, DateTime end)
    {
        using var connection = connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT b.SessionDate,
                   v.Version,
                   SUM(b.Volume) AS TotalVolume,
                   (SELECT f.Open
                      FROM BarData f
                     WHERE f.Instrument = b.Instrument
                       AND f.VersionId = b.VersionId
                       AND f.SessionDate = b.SessionDate
                     ORDER BY f.Timestamp
                     LIMIT 1) AS FirstOpen
              FROM BarData b
              JOIN InstrumentVersion v ON v.Id = b.VersionId
             WHERE b.Instrument = @Instrument
               AND b.SessionDate BETWEEN @Start AND @End
             GROUP BY b.SessionDate, b.VersionId";

        SqlHelper.AddParameter(cmd, "@Instrument", instrument);
        SqlHelper.AddParameter(cmd, "@Start", start.ToString(AppConstants.DATE_FORMAT));
        SqlHelper.AddParameter(cmd, "@End", end.ToString(AppConstants.DATE_FORMAT));

        await connection.OpenAsync();

        var rows = new List<DailyVersion>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new DailyVersion
            {
                SessionDate = reader.GetString(0),
                Version = reader.GetString(1),
                Volume = reader.GetDouble(2),
                FirstOpen = reader.GetDouble(3)
            });
        }

        return rows;
    }

    private static DateTime parseVersion(string version) =>
        DateTime.ParseExact(version, VERSION_FORMAT, CultureInfo.InvariantCulture);
}
