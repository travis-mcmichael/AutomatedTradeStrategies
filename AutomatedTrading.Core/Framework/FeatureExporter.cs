
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutomatedTrading.Core.Data.Queries;
using AutomatedTrading.Core.Framework.Logging;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Framework;

/// <summary>
/// Config-driven feature exporter. Reads the feature list from a <see cref="ModelEntry"/>
/// and computes each feature via <see cref="FeatureComputations.ParseFeatures"/>, eliminating
/// the need for a hand-written exporter per model.
///
/// Session open vs RTH open is inferred automatically: if any feature declares
/// <c>source: "currentMinute"</c> the model is treated as trade-day (RTH open anchor);
/// otherwise it is treated as session-day (session open anchor).
///
/// Target direction is inferred from the model name suffix: <c>.high</c> targets
/// the upside move above open; <c>.low</c> targets the downside move below open.
/// Both are normalised by ATR(20).
/// </summary>
public class FeatureExporter
{
    private const int MinLookback = 20;
    private readonly ModelConfig _model;
    private readonly string _outputPath;
    private readonly StreamBarsInRange _streamBars;
    private readonly GetVersionChangeovers _getChangeovers;

    public FeatureExporter(ModelConfig model, string outputPath, StreamBarsInRange streamBars, GetVersionChangeovers getVersionChangeovers)
    {
        _model = model;
        _outputPath = outputPath;
        _streamBars = streamBars;
        _getChangeovers = getVersionChangeovers;
    }

    // Returns the number of feature rows written. Errors while building features are logged and rethrown.
    public async Task<int> GenerateFeatures(DateTime start, DateTime end)
    {
        if (_model.ModelFeatures.Count == 0)
        {
            Logger.Info($"  No features configured for '{_model.ModelName}'. Skipping.");
            return 0;
        }
        Logger.Info("  Loading 1-minute bars from database...");
        var minuteBarsBySession = new Dictionary<string, List<BarData>>();
        // collect the minute bars by session date
        await GetBarsBySession(start, end, minuteBarsBySession!);

        if (!minuteBarsBySession.Any()) return 0;

        // get keys in order by date
        var orderedSessions = minuteBarsBySession.Keys.OrderByDescending(x => x).ToList();

        // changeover - in this context, a changeover occurs on a session date when:
        // - 2 instrument versions have bar data on the session date
        // - the total volume of the new instrument version surpasses the total volume of the old version's bar data
        // we loop backwards through session dates and adjust all session dates beyond each changeover by the gap between the two versions' open values.
        // This is to prevent odd price jumps when switching between versions of an instrument.
        var changeovers = await _getChangeovers.Execute(_model.Instrument, start, end);
        if (changeovers.Any())
        {
            HandleChangeoverAdjustment(changeovers, orderedSessions, minuteBarsBySession);
        }

        Logger.Info($"  Loaded {minuteBarsBySession.Count} sessions. Building features...");
        // Oldest first, so rows are written in chronological order. Built after the changeover step, which can drop sessions.
        var sessionDates = minuteBarsBySession.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
        var rowsWritten = 0;
        using var writer = new StreamWriter(_outputPath);
        try
        {
            var header = string.Join(",", _model.ModelFeatures.Select(columnName));
            await writer.WriteLineAsync(header + "," + targetColumnName());
            rowsWritten = await buildFeatures(minuteBarsBySession, sessionDates, rowsWritten, writer);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Error building features : {ex.Message}\r\n\t{ex.StackTrace}");
            throw;
        }

        Console.WriteLine($"  Feature export complete. {rowsWritten} rows written to {_outputPath}");
        return rowsWritten;
    }

    // Produces a unique column name for each feature, appending sorted param values
    // when present so that e.g. Return(lookback=1) → "Return_1" and
    // SmaRatio(fast=5,slow=20) → "SmaRatio_5_20".
    private static string columnName(ModelFeature feature)
    {
        if (feature.ModelFeatureParams is null || feature.ModelFeatureParams.Count() == 0)
            return feature.FeatureType?.Name ?? "";

        var suffix = string.Join("_", feature.ModelFeatureParams.OrderBy(kv => kv.FeatureTypeParam?.ParameterKey).Select(kv => kv.ParamValue));
        return $"{feature.FeatureType?.Name}_{suffix}";
    }

    private double getTarget(BarData barData, double atr)
    {
        switch (_model.Target)
        {
            case "HIGH": //Core.Framework.Enums.PredictionTarget.HIGH:
                return (barData.High - barData.Open) / atr;
            case "LOW": //Core.Framework.Enums.PredictionTarget.LOW:
                return (barData.Open - barData.Low) / atr;
        }
        return 0;
    }

    private string targetColumnName() => _model.Target;

    // Handles both straight windows (e.g. 08:30–16:00) and cross-midnight windows
    // (e.g. 17:00–08:30) where start > end.
    private static bool InWindow(int time, int start, int end) =>
        start <= end ? time >= start && time < end
                     : time >= start || time < end;

    private async Task<int> buildFeatures(Dictionary<string, List<BarData>> minuteBarsBySession,
        List<string> sessionDates,
        int written,
        StreamWriter writer)
    {
        // One entry per session that has bars in the model's window (or the whole session), oldest first. Each bar is
        // stored with its date so a session without window bars can't push the other rows out of line with their dates.
        var days = new List<(string Date, BarData Bar)>();
        if (_model.WindowStart != null && _model.WindowEnd != null)
        {
            var start = int.Parse(_model.WindowStart!);
            var end = int.Parse(_model.WindowEnd!);
            foreach (var date in sessionDates)
            {
                if (!minuteBarsBySession.ContainsKey(date)) continue;
                if (!TradingCalendar.IsTradingDay(date)) continue;
                var windowBars = minuteBarsBySession[date].Where(bar => InWindow(int.Parse(bar.SessionTime!), start, end)).ToList();
                if (windowBars.Count > 0)
                    days.Add((date, BarData.Aggregate(windowBars)));
            }
        }
        else
        {
            foreach (var date in sessionDates)
                days.Add((date, BarData.Aggregate(minuteBarsBySession[date])));
        }

        if (days.Count == 0 || days.Count < MinLookback) return written;
        for (var i = MinLookback; i < days.Count; i++)
        {
            var (date, bar) = days[i];

            // ATR as of this row: the MinLookback true ranges ending at this row's bar (needs MinLookback + 1 bars).
            var atr20 = Computations.ATR(days.GetRange(i - MinLookback, MinLookback + 1).Select(d => d.Bar).ToList(), MinLookback);
            if (double.IsNaN(atr20) || atr20 == 0) continue;

            var target = getTarget(bar, atr20);
            var featureValues = FeatureComputations.ParseFeatures(
               _model.ModelFeatures,
               new FeatureContext
               (
                   atr20,
                   date,
                   minuteBarsBySession
               ));
            if (featureValues != null)
            {
                await writer.WriteLineAsync($"{string.Join(",", featureValues.Select(f => f.ToString("G6")))},{target:G6}");
                written++;
            }
            if (written % 100 == 0)
                Console.WriteLine($"  {written} rows written...");
        }
        return written;
    }

    private async Task GetBarsBySession(DateTime start, DateTime end, Dictionary<string, List<BarData>?> minuteBarsBySession)
    {
        await _streamBars.ExecuteAsync(_model.Instrument, start, end, bar =>
        {
            if (bar.SessionDate == null) return Task.CompletedTask;
            if (!minuteBarsBySession.TryGetValue(bar.SessionDate, out var list))
            {
                list = [];
                minuteBarsBySession[bar.SessionDate] = list;
            }
            list?.Add(bar);
            return Task.CompletedTask;
        });
    }

    // Builds a back-adjusted continuous series: each session keeps only its active contract's bars
    // (the old version before a roll date, the new version from it onward), shifted by the sum of every
    // roll after it so prices line up with the newest contract. Sessions left with no bars are dropped.
    private static void HandleChangeoverAdjustment(IList<VersionChangeover> changeovers, IList<string> orderedSessions, Dictionary<string, List<BarData>> minuteBarsBySession)
    {
        var rolls = changeovers.OrderBy(c => c.SessionDate, StringComparer.Ordinal).ToList();
        foreach (var session in orderedSessions)
        {

            var dt = int.Parse(session);
            var activeVersion = rolls.LastOrDefault(c => dt >= int.Parse(c.SessionDate))?.NewVersion ?? rolls.First().OldVersion;
            var adjustment = rolls.Where(c => int.Parse(c.SessionDate) > dt).Sum(c => c.Adjustment);

            var bars = minuteBarsBySession[session].Where(b => b.InstrumentVersion == activeVersion).ToList();
            if (bars.Count == 0)
            {
                minuteBarsBySession.Remove(session);
                continue;
            }

            if (adjustment == 0)
                continue;

            foreach (var bar in bars)
            {
                bar.Open += adjustment;
                bar.High += adjustment;
                bar.Low += adjustment;
                bar.Close += adjustment;
            }
            minuteBarsBySession[session] = bars;
        }
    }
}
