using AutomatedTrading.Core.Framework.Config;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.Logging;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Export.Feature.Exporters;

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
    private readonly ModelEntry _model;
    private readonly string _outputPath;
    private readonly IBarRepository _barRepository;

    public FeatureExporter(ModelEntry model, string outputPath, IBarRepository barRepository)
    {
        _model = model;
        _outputPath = outputPath;
        _barRepository = barRepository;
    }

    public async Task GenerateFeatures(DateTime start, DateTime end)
    {
        if (_model.Features.Count == 0)
        {
            Logger.Info($"  No features configured for '{_model.ModelName}'. Skipping.");
            return;
        }

        Logger.Info("  Loading 1-minute bars from database...");
        var minuteBarsBySession = new Dictionary<string, List<BarData>>();

        await _barRepository.StreamBarsAsync(start, end, bar =>
        {
            if (bar.SessionDate == null) return Task.CompletedTask;
            if (!minuteBarsBySession.TryGetValue(bar.SessionDate, out var list))
            {
                list = new List<BarData>();
                minuteBarsBySession[bar.SessionDate] = list;
            }
            list.Add(bar);
            return Task.CompletedTask;
        });

        Logger.Info($"  Loaded {minuteBarsBySession.Count} sessions. Building features...");

        var sessionDates = minuteBarsBySession.Keys.Where(x =>
        {
            var day = DateTime.ParseExact(x, "yyyyMMdd", null).DayOfWeek;
            return day != DayOfWeek.Sunday && day != DayOfWeek.Saturday;
        }).OrderBy(x => x).ToList();
        
        var rowsWritten = 0;
        var errDate = string.Empty;

        double[]? featureValues = null;
        double? target = null;
        using var writer = new StreamWriter(_outputPath);
        try
        {
            var header = string.Join(",", _model.Features.Select(columnName));
            await writer.WriteLineAsync(header + "," + targetColumnName());
            if (_model.BarType == "session")
            {
                var dailyBars = sessionDates
                    .Select(d => BarData.Aggregate(minuteBarsBySession[d]))
                    .ToList();
                for (var i = MinLookback; i < sessionDates.Count; i++)
                {
                    errDate = sessionDates[i];
                    var atr20 = Computations.ATR(dailyBars, i - 1, MinLookback);
                    if (atr20 == 0) continue;
                    featureValues = buildSessionFeatures(dailyBars, i, atr20, sessionDates, minuteBarsBySession);
                    target = getTarget(minuteBarsBySession[sessionDates[i]], dailyBars[i].Open, atr20);
                    if (featureValues != null)
                        await writer.WriteLineAsync(string.Join(",", featureValues.Select(f => f.ToString("G6"))) + "," + target?.ToString("G6"));
                    rowsWritten++;
                    if (rowsWritten % 100 == 0)
                        Console.WriteLine($"  {rowsWritten} rows written...");
                }
            }
            else if (_model.BarType == "window")
            {
                var windowStart = int.Parse(_model.WindowStart);
                var windowEnd = int.Parse(_model.WindowEnd);
                var dailyBars = sessionDates.Select(d =>
                {
                    var minuteBarsForDay = minuteBarsBySession[d];
                    var windowBars = minuteBarsForDay.Where(b =>
                        InWindow(int.Parse(b.SessionTime), windowStart, windowEnd));
                    if (windowBars.Any())
                        return BarData.Aggregate(windowBars.OrderBy(b => b.Timestamp).ToList());
                    return null;
                })
                .Where(b => b != null)
                .OrderBy(b => b.Timestamp)
                .ToList();
                if (!dailyBars.Any()) return;
                for (var i = MinLookback; i < dailyBars.Count; i++)
                {
                    errDate = dailyBars[i].SessionDate;
                    var atr20 = Computations.ATR(dailyBars, i - 1, MinLookback);
                    if (atr20 == 0) continue;
                    var open = dailyBars[i].Open;
                    target = getTarget(minuteBarsBySession[dailyBars[i].SessionDate], open, atr20);
                    featureValues = buildWindowFeatures(dailyBars, windowStart, windowEnd, i, atr20, open, dailyBars[i].SessionDate, dailyBars[i - 1].SessionDate, minuteBarsBySession);
                    if (featureValues != null)
                        await writer.WriteLineAsync(string.Join(",", featureValues.Select(f => f.ToString("G6"))) + "," + target?.ToString("G6"));
                    rowsWritten++;
                    if (rowsWritten % 100 == 0)
                        Console.WriteLine($"  {rowsWritten} rows written...");
                }
            }
            else if (_model.BarType == "minute")
            {
                var dailyBars = sessionDates
                    .Select(d => BarData.Aggregate(minuteBarsBySession[d]))
                    .ToList();
                for (var i = MinLookback; i < sessionDates.Count; i++)
                {
                    errDate = sessionDates[i];
                    var sessionBars = minuteBarsBySession[sessionDates[i]];
                    var atr20 = Computations.ATR(dailyBars, i - 1, MinLookback);
                    if (atr20 == 0) continue;
                    var open = sessionBars.First().Open;
                    target = getTarget(sessionBars, open, atr20);
                    featureValues = buildMinuteFeatures(dailyBars, sessionBars, i, atr20, open, sessionDates, minuteBarsBySession);
                    if (featureValues != null)
                        await writer.WriteLineAsync(string.Join(",", featureValues.Select(f => f.ToString("G6"))) + "," + target?.ToString("G6"));
                    rowsWritten++;
                    if (rowsWritten % 100 == 0)
                        Console.WriteLine($"  {rowsWritten} rows written...");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Error on session {errDate}: {ex.Message}");
        }

        Console.WriteLine($"  Feature export complete. {rowsWritten} rows written to {_outputPath}");
    }

    // Produces a unique column name for each feature, appending sorted param values
    // when present so that e.g. Return(lookback=1) → "Return_1" and
    // SmaRatio(fast=5,slow=20) → "SmaRatio_5_20".
    private static string columnName(ModelFeature feature)
    {
        if (feature.Params is null || feature.Params.Count == 0)
            return feature.Name;

        var suffix = string.Join("_", feature.Params.OrderBy(kv => kv.Key).Select(kv => kv.Value));
        return $"{feature.Name}_{suffix}";
    }

    private double getTarget(List<BarData> barData, double open, double atr)
    {
        switch (_model.Target)
        {
            case "HIGH": //Core.Framework.Enums.PredictionTarget.HIGH:
                return (barData.Max(b => b.High) - open) / atr;
            case "LOW": //Core.Framework.Enums.PredictionTarget.LOW:
                return (open - barData.Min(b => b.Low)) / atr;
        }
        return 0;
    }

    private string targetColumnName() => $"{_model.BarType} {_model.Target}";

    // Handles both straight windows (e.g. 08:30–16:00) and cross-midnight windows
    // (e.g. 17:00–08:30) where start > end.
    private static bool InWindow(int time, int start, int end) =>
        start <= end ? time >= start && time < end
                     : time >= start || time < end;

    private double[]? buildWindowFeatures(List<BarData> windowBars, int start, int end, int i, double atr, double open, string currentDate, string prevDate, Dictionary<string, List<BarData>> minuteBarsBySession)
    {
        var prev = minuteBarsBySession[prevDate].Where(b =>
            InWindow(int.Parse(b.SessionTime), start, end))
        .OrderBy(b => b.Timestamp)
        .ToList();
        var current = minuteBarsBySession[currentDate].Where(b =>
            InWindow(int.Parse(b.SessionTime), start, end))
        .OrderBy(b => b.Timestamp)
        .ToList();
        var ctx = new FeatureContext
        (
            windowBars,
            i - 1,
            windowBars[i - 1],
            open,
            atr,
            currentDate,
            prev,
            current
        );
        return FeatureComputations.ParseFeatures(_model.Features, ctx);
    }

    private double[] buildMinuteFeatures(List<BarData> dailyBars, List<BarData> sessionBars, int i, double atr, double open, List<string> sessionDates, Dictionary<string, List<BarData>> minuteBarsBySession)
    {
        return FeatureComputations.ParseFeatures(_model.Features, new FeatureContext
        (
            dailyBars,
            i - 1,
            dailyBars[i - 1],
            open,
            atr,
            sessionDates[i],
            minuteBarsBySession[sessionDates[i - 1]],
            sessionBars
        ));
    }

    private double[]? buildSessionFeatures(List<BarData> dailyBars, int i, double atr, List<string> sessionDates, Dictionary<string, List<BarData>> minuteBarsBySession)
    {
        return FeatureComputations.ParseFeatures(_model.Features, new FeatureContext
        (
            dailyBars,
            i - 1,
            dailyBars[i - 1],
            dailyBars[i].Open,
            atr,
            sessionDates[i],
            minuteBarsBySession[sessionDates[i - 1]],
            minuteBarsBySession[sessionDates[i]]
        ));
    }
}
