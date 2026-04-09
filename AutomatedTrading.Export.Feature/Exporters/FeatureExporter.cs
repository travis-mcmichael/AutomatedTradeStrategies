using AutomatedTrading.Core.Framework.Config;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
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
            Console.WriteLine($"  No features configured for '{_model.ModelName}'. Skipping.");
            return;
        }

        Console.WriteLine("  Loading 1-minute bars from database...");
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

        Console.WriteLine($"  Loaded {minuteBarsBySession.Count} sessions. Building features...");

        var sessionDates = minuteBarsBySession.Keys.OrderBy(x => x).ToList();
        var dailyBars = sessionDates
            .Select(d => BarData.Aggregate(minuteBarsBySession[d]))
            .ToList();

        var headerWritten = false;
        var rowsWritten = 0;

        double[]? featureValues = null;
        double? target = null;
        using var writer = new StreamWriter(_outputPath);
        for (var i = MinLookback; i < sessionDates.Count; i++)
        {
            try
            {
                var j = i - 1;
                if (_model.BarType == "session")
                {
                    var atr20 = Computations.ATR(dailyBars, j, 20);
                    if (atr20 == 0) continue;
                    featureValues = buildSessionFeatures(dailyBars, i, atr20, sessionDates, minuteBarsBySession);
                    target = getTarget(dailyBars, dailyBars[i].Open, atr20);
                }
                else if (_model.BarType == "window")
                {
                    var windowStart = int.Parse(_model.WindowStart);
                    var windowEnd = int.Parse(_model.WindowEnd);
                    var windowBars = sessionDates.Select(d => 
                        BarData.Aggregate(minuteBarsBySession[d]
                            .Where(b => 
                                int.Parse(b.SessionTime) >= windowStart && 
                                int.Parse(b.SessionTime) < windowEnd)
                            .OrderBy(b => b.Timestamp)
                            .ToList()))
                        .ToList();
                    if (!windowBars.Any()) continue;
                    var atr20 = Computations.ATR(windowBars, j, MinLookback);
                    if (atr20 == 0) continue;
                    var open = windowBars[i].Open;
                    target = getTarget(windowBars, open, atr20);
                    featureValues = buildWindowFeatures(windowBars, windowStart, windowEnd, i, atr20, open, sessionDates, minuteBarsBySession);
                }
                else if (_model.BarType == "minute")
                {
                    var sessionBars = minuteBarsBySession[sessionDates[i]];
                    var atr20 = Computations.ATR(sessionBars, j, 20);
                    if (atr20 == 0) continue;
                    var open = sessionBars.First().Open;
                    target = getTarget(sessionBars, open, atr20);
                    featureValues = buildMinuteFeatures(sessionBars, i, atr20, open, sessionDates, minuteBarsBySession);
                }
                if (!headerWritten)
                {
                    var header = string.Join(",", _model.Features.Select(columnName));
                    await writer.WriteLineAsync(header + "," + targetColumnName());
                    headerWritten = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error on session {sessionDates[i]}: {ex.Message}");
            }

            if (featureValues != null)
            {
                await writer.WriteLineAsync(
                string.Join(",", featureValues.Select(f => f.ToString("G6"))) +
                    "," + target?.ToString("G6"));
            }

            rowsWritten++;
            if (rowsWritten % 100 == 0)
                Console.WriteLine($"  {rowsWritten} rows written...");
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
            case Core.Framework.Enums.PredictionTarget.HIGH:
                return (barData.Max(b => b.High) - open) / atr;
            case Core.Framework.Enums.PredictionTarget.LOW:
                return (open - barData.Min(b => b.Low)) / atr;
        }
        return 0;
    }

    private string targetColumnName() => $"{_model.BarType} {_model.Target}";

    private double[]? buildWindowFeatures(List<BarData> windowBars, int start, int end, int i, double atr, double open, List<string> sessionDates, Dictionary<string, List<BarData>> minuteBarsBySession)
    {        
        var prev = minuteBarsBySession[sessionDates[i - 1]].Where(b => 
            int.Parse(b.SessionDate) >= start && 
            int.Parse(b.SessionTime)< end)
        .OrderBy(b => b.Timestamp)
        .ToList();
        var current = minuteBarsBySession[sessionDates[i]].Where(b => 
            int.Parse(b.SessionDate) >= start && 
            int.Parse(b.SessionTime)< end)
        .OrderBy(b => b.Timestamp)
        .ToList(); 
        var ctx = new FeatureContext
        (
            windowBars,
            i - 1,
            windowBars[i - 1],
            open,
            atr,
            sessionDates[i],
            prev,
            current
        );
        return FeatureComputations.ParseFeatures(_model.Features, ctx);
    }

    private double[] buildMinuteFeatures(List<BarData> bars, int i, double atr, double open, List<string> sessionDates, Dictionary<string, List<BarData>?> minuteBarsBySession)
    {
        return FeatureComputations.ParseFeatures(_model.Features, new FeatureContext
        (
            bars,
            i - 1,
            bars.Last(),
            open,
            atr,
            sessionDates[i],
            minuteBarsBySession[sessionDates[i - 1]],
            bars
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
            minuteBarsBySession[sessionDates[i]],
            minuteBarsBySession[sessionDates[i - 1]]
        ));
    }
}
