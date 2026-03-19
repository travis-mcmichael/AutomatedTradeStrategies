// See https://aka.ms/new-console-template for more information
using AutomatedTrading.Core.Api.Evaluators;
using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Console;

class Program
{
    static string model = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models\\20260318", "xgb_model.onnx");
    static string scaler = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models\\20260318", "scaler_params.json");
    const int featureCount = 34;
    static int YEAR = 2026;
    static int MONTH = 3;
    static int DAY = 16;
    static DateTime sessionDate = DateTime.Today.AddDays(-1); // new DateTime(YEAR, MONTH, DAY);
    static async Task Main(string[] args)
    {
        using var evaluator = new BaseMLEvaluator(model, scaler, featureCount);
        evaluator.LoadModel();
        if (!evaluator.SessionLoaded())
        {
            System.Console.WriteLine("Model failed to load");
            return;
        }
        
        var yesterday = getPreviousSessionDate(sessionDate.ToString("yyyyMMdd"));
        var bars = await SqliteDataLayer.GetBarsBySessionDate(yesterday);
        var prevBars = await SqliteDataLayer.GetBarsBySessionDate(getPreviousSessionDate(yesterday));
        if (bars.Count == 0 || prevBars.Count == 0)
        {
            System.Console.WriteLine("Missing bar data");
            return;
        }
        var today = aggregateSession(bars);
        var prev = aggregateSession(prevBars);

        var sequence = await LoadRecentDailyBars(yesterday, 30);
        var i = sequence.Count - 1;
        var atr10 = Computations.ATR(sequence, i, 10);
        var atr20 = Computations.ATR(sequence, i, 20);
        var vwap = Computations.SessionVWAP(bars, i);
        var sma5 = Computations.SMA(sequence, i, 5, x => x.Close);
        var sma10 = Computations.SMA(sequence, i, 10, x => x.Close);
        var sma20 = Computations.SMA(sequence, i, 20, x => x.Close);
        var avgRange10 = Computations.AverageRange(sequence, i, 10);
        var rangeSoFar = today.High - today.Low;
        var featureVector = new double[]
        {
            prev.High,
            prev.Low,
            prev.High - prev.Low,
            Math.Abs(prev.Close - prev.Open),
            prev.High - Math.Max(prev.Open, prev.Close),
            Math.Min(prev.Open, prev.Close) - prev.Low,
            safeDiv(prev.Close - prev.Low, prev.High - prev.Low),
            Computations.ATR(sequence, i, 5),
            atr10,
            atr20,
            Computations.AverageRange(sequence, i, 5),
            avgRange10,
            safeDiv(prev.High - prev.Low, avgRange10),
            Computations.Return(sequence, i, 1),
            Computations.Return(sequence, i, 3),
            Computations.Return(sequence, i, 5),
            sma5,
            sma10,
            sma20,
            safeDiv(today.Close, sma5),
            safeDiv(today.Close, sma10),
            safeDiv(today.Close, sma20),
            Computations.SMA(sequence, i, 20, x => x.Volume),
            Computations.VolumeZScore(sequence, i, 20),
            safeDiv(today.Close - prev.High, atr20),
            safeDiv(today.Close - prev.Low, atr20),
            safeDiv(rangeSoFar, atr20),
            safeDiv(atr10, atr20),
            safeDiv(today.Close - vwap, atr20),
            safeDiv(today.Open - prev.Close, prev.Close),
            safeDiv(today.High - today.Low, prev.Close),
            safeDiv(today.Open - prev.Close, atr20),

            today.Timestamp.DayOfWeek == DayOfWeek.Sunday ? 0 : (int)today.Timestamp.DayOfWeek,
            today.Timestamp.Day
        };
        var scaled= applyScaler(featureVector, evaluator);
        var prediction = evaluator.RunInference(scaled);

        System.Console.WriteLine($"Prediction - {prediction} * ATR20 {atr20} = {prediction * atr20}");
        System.Console.WriteLine("Finished");
    }

    static BarData aggregateSession(List<BarData> bars)
    {
        return new BarData
        {
            Timestamp = bars.First().Timestamp,
            Open = bars.First().Open,
            Close = bars.Last().Close,
            High = bars.Max(x => x.High),
            Low = bars.Min(x => x.Low),
            Volume = bars.Sum(x => x.Volume)
        };
    }

    static async Task<List<BarData>> LoadRecentDailyBars(string sessionDate, int lookbackDays)
    {
                var list = new List<BarData>();

        var date = DateTime.ParseExact(sessionDate, "yyyyMMdd", null);

        for (int i = lookbackDays; i >= 0; i--)
        {
            var d = date.AddDays(-i).ToString("yyyyMMdd");
            var bars = await SqliteDataLayer.GetBarsBySessionDate(d);
            if (bars.Count == 0) continue;

            list.Add(aggregateSession(bars));
        }

        return list;
    }

    static double[] applyScaler(double[] features, BaseMLEvaluator evaluator)
    {
        if (!evaluator.ScalerLoaded)
            return features;
        var scaled = new double[features.Length];

        // reflection hack (since fields are private)
        var meanField = typeof(BaseMLEvaluator).GetField("scalerMean", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var scaleField = typeof(BaseMLEvaluator).GetField("scalerScale", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var mean = (double[])meanField.GetValue(evaluator);
        var scale = (double[])scaleField.GetValue(evaluator);

        for (int i = 0; i < features.Length; i++)
        {
            if (scale[i] == 0)
                scaled[i] = 0;
            else
                scaled[i] = (features[i] - mean[i]) / scale[i];
        }

        return scaled;
    }

    static double safeDiv(double a, double b)
    {
        if (b == 0) return 0;
        return a / b;
    }

    static string getPreviousSessionDate(string sessionDate)
    {
        var dt = DateTime.ParseExact(sessionDate, "yyyyMMdd", null);
        switch (dt.DayOfWeek)
        {
            case DayOfWeek.Monday:
            return dt.AddDays(-3).ToString("yyyyMMdd");
            case DayOfWeek.Sunday:
            return dt.AddDays(-2).ToString("yyyyMMdd");
            default:
            return dt.AddDays(-1).ToString("yyyyMMdd");
        }
    }
}