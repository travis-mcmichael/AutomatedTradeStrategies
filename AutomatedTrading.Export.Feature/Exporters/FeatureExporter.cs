using AutomatedTrading.Core.Framework;
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

        using var writer = new StreamWriter(_outputPath);

        for (int i = MinLookback; i < sessionDates.Count; i++)
        {
            var j = i - 1; // index of most recent complete session in dailyBars

            var currentMinuteBars = minuteBarsBySession[sessionDates[i]];
            var prevMinuteBars = minuteBarsBySession[sessionDates[j]];

            if (!currentMinuteBars.Any() || !prevMinuteBars.Any()) continue;

            try
            {
                var atr20 = Computations.ATR(dailyBars, j, 20);
                if (atr20 == 0) continue;

                var current = dailyBars[j];
                var orderedCurrent = currentMinuteBars.OrderBy(b => b.Timestamp).ToList();

                double open;
                double target;
                List<BarData>? currentBarsForCtx;

                if (_model.BarType == "window")
                {
                    var windowStart = int.Parse(_model.WindowStart);
                    var windowEnd = int.Parse(_model.WindowEnd);

                    var windowBars = orderedCurrent
                        .Where(b => int.Parse(b.SessionTime) > windowStart && int.Parse(b.SessionTime) < windowEnd)
                        .ToList();
                    if (!windowBars.Any()) continue;
                    open = windowBars.First().Open;
                    target = getTarget(windowBars, open, atr20);
                    currentBarsForCtx = orderedCurrent;
                }
                else
                {
                    open = orderedCurrent.First().Open;
                    target = getTarget(currentMinuteBars, open, atr20);
                    currentBarsForCtx = null;
                }


                var ctx = new FeatureContext(
                    dailyBars,
                    j,
                    current,
                    open,
                    atr20,
                    sessionDates[i],
                    prevMinuteBars,
                    currentBarsForCtx
                );

                var featureValues = FeatureComputations.ParseFeatures(_model.Features, ctx);

                if (!headerWritten)
                {
                    var header = string.Join(",", _model.Features.Select(columnName));
                    await writer.WriteLineAsync(header + "," + targetColumnName());
                    headerWritten = true;
                }

                await writer.WriteLineAsync(
                    string.Join(",", featureValues.Select(f => f.ToString("G6"))) +
                    "," + target.ToString("G6")
                );

                rowsWritten++;
                if (rowsWritten % 100 == 0)
                    Console.WriteLine($"  {rowsWritten} rows written...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error on session {sessionDates[i]}: {ex.Message}");
            }
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
                return barData.Max(b => b.High) - open / atr;
            case Core.Framework.Enums.PredictionTarget.LOW:
                return (open - barData.Min(b => b.Low)) / atr;
        }
        return 0;
    }

    private string targetColumnName() => $"{_model.BarType} {_model.Target}";
}
