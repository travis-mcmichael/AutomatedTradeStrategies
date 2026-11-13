using System;
using System.Collections.Generic;
using System.Linq;
using AutomatedTrading.Core.Framework.Logging;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Framework;

// Aggregates of prior sessions, computed once for a bar snapshot instead of once per feature per row. A session's
// value is its full bars (or its bars inside a window). Sessions are keyed by yyyyMMdd, which sorts chronologically.
// A feature's current session is excluded here: FeatureContext cuts it per row and aggregates it itself.
// Not thread-safe: build one per export or per inference call, and do not change the snapshot after building it.
public class SessionAggregateCache
{
    private sealed class WindowSeries
    {
        public readonly List<string> Keys = new();
        public readonly List<BarData> Aggregates = new();
    }

    private readonly Dictionary<string, List<BarData>> _bars;
    // Sessions that have bars, ascending. _sessionAggregates is aligned with it.
    private readonly List<string> _keys = new();
    private readonly List<BarData> _sessionAggregates = new();
    private readonly Dictionary<(int Start, int End), WindowSeries> _windows = new();

    public SessionAggregateCache(Dictionary<string, List<BarData>> bars)
    {
        _bars = bars;
        foreach (var key in bars.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (bars[key].Count == 0)
            {
                Logger.Warn($"No data found for {key}. Verify data and session before using.");
                continue;
            }
            _keys.Add(key);
            _sessionAggregates.Add(BarData.Aggregate(bars[key]));
        }
    }

    /// <summary>Session aggregates for sessions dated on or before throughDate, oldest first, without excludeDate.</summary>
    public List<BarData> SessionAggregates(string throughDate, string excludeDate) =>
        Through(_keys, _sessionAggregates, throughDate, excludeDate);

    /// <summary>Window aggregates (bars inside start-end) for sessions dated on or before throughDate, without excludeDate.</summary>
    public List<BarData> WindowAggregates(int start, int end, string throughDate, string excludeDate)
    {
        var series = windowSeries(start, end);
        return Through(series.Keys, series.Aggregates, throughDate, excludeDate);
    }

    private WindowSeries windowSeries(int start, int end)
    {
        if (_windows.TryGetValue((start, end), out var cached))
            return cached;

        var window = new TradeWindow { WindowStart = start, WindowEnd = end };
        var series = new WindowSeries();
        for (var i = 0; i < _keys.Count; i++)
        {
            var inWindow = _bars[_keys[i]].Where(x => window.InWindow(int.Parse(x.SessionTime))).ToList();
            if (inWindow.Count == 0) continue;
            series.Keys.Add(_keys[i]);
            series.Aggregates.Add(BarData.Aggregate(inWindow));
        }
        _windows[(start, end)] = series;
        return series;
    }

    private static List<BarData> Through(List<string> keys, List<BarData> aggregates, string throughDate, string excludeDate)
    {
        var count = CountThrough(keys, throughDate);
        var result = aggregates.GetRange(0, count);
        if (excludeDate != null)
        {
            var index = keys.BinarySearch(excludeDate, StringComparer.Ordinal);
            if (index >= 0 && index < count)
                result.RemoveAt(index);
        }
        return result;
    }

    // Number of keys that are <= date. Keys are sorted ascending and unique.
    private static int CountThrough(List<string> keys, string date)
    {
        var index = keys.BinarySearch(date, StringComparer.Ordinal);
        return index >= 0 ? index + 1 : ~index;
    }
}
