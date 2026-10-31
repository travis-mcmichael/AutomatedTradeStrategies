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
// exceeds the older version's. The adjustment is the difference between the two versions' Open at the
// first minute on that date where both traded, falling back to each version's first Open of the day
// if they never share a minute.
public class GetVersionChangeovers(IDbConnectionFactory connectionFactory)
{
    private const string VERSION_FORMAT = "MM-yy";

    private class DailyVersion
    {
        public string SessionDate = default!;
        public string Version = default!;
        public double Volume;
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
                Adjustment = await loadAdjustment(instrument, versions[i - 1], versions[i], changeoverDate)
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
                   b.InstrumentVersion,
                   SUM(b.Volume) AS TotalVolume
              FROM BarData b
             WHERE b.Instrument = @Instrument
               AND b.SessionDate BETWEEN @Start AND @End
             GROUP BY b.SessionDate, b.InstrumentVersion";

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
                Volume = reader.GetDouble(2)
            });
        }

        return rows;
    }

    // New version's Open minus old version's Open at the earliest minute on sessionDate where both have a bar.
    private async Task<double> loadAdjustment(string instrument, string oldVersion, string newVersion, string sessionDate)
    {
        using var connection = connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT n.Open - o.Open
              FROM BarData o
              JOIN BarData n
                ON n.Instrument = o.Instrument
               AND n.Timestamp = o.Timestamp
             WHERE o.Instrument = @Instrument
               AND o.SessionDate = @SessionDate
               AND o.InstrumentVersion = @OldVersion
               AND n.InstrumentVersion = @NewVersion
             ORDER BY o.Timestamp
             LIMIT 1";

        SqlHelper.AddParameter(cmd, "@Instrument", instrument);
        SqlHelper.AddParameter(cmd, "@SessionDate", sessionDate);
        SqlHelper.AddParameter(cmd, "@OldVersion", oldVersion);
        SqlHelper.AddParameter(cmd, "@NewVersion", newVersion);

        await connection.OpenAsync();

        var shared = await cmd.ExecuteScalarAsync();
        if (shared is not null && shared is not DBNull)
            return Convert.ToDouble(shared);

        cmd.CommandText = @"
            SELECT (SELECT Open FROM BarData
                     WHERE Instrument = @Instrument AND SessionDate = @SessionDate AND InstrumentVersion = @NewVersion
                     ORDER BY Timestamp LIMIT 1)
                 - (SELECT Open FROM BarData
                     WHERE Instrument = @Instrument AND SessionDate = @SessionDate AND InstrumentVersion = @OldVersion
                     ORDER BY Timestamp LIMIT 1)";
        return Convert.ToDouble(await cmd.ExecuteScalarAsync());
    }

    private static DateTime parseVersion(string version) =>
        DateTime.ParseExact(version, VERSION_FORMAT, CultureInfo.InvariantCulture);
}
