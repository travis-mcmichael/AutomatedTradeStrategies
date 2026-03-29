using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.ML;
using AutomatedTrading.Core.Framework.Predictors;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Console.InferenceTesting;

/// <summary>
/// Implements <see cref="IBarHighPredictor"/> for the Console project by running
/// inference directly against the local ONNX model, using the same feature
/// engineering pipeline as <see cref="InferenceEvaluationTest"/>.
/// </summary>
public class ConsoleBarHighPredictor : IBarHighPredictor, IDisposable
{
    private readonly IBarRepository _barRepo;
    private readonly BaseMLEvaluator _highEval;

    public ConsoleBarHighPredictor(IBarRepository barRepo, ModelConfig highConfig)
    {
        _barRepo = barRepo;

        var dir = Path.Combine(AppConstants.MODEL_DIR, highConfig.ModelDate, highConfig.Name);
        _highEval = new BaseMLEvaluator(
            Path.Combine(dir, AppConstants.XGB_MODEL_FILENAME),
            Path.Combine(dir, AppConstants.SCALER_FILENAME),
            highConfig.FeatureCount);
        _highEval.LoadModel();

        if (!_highEval.SessionLoaded())
            throw new InvalidOperationException($"Model '{highConfig.Name}' failed to load.");
    }

    public async Task<BarHighPrediction?> PredictSessionHighAsync(string sessionDate, double sessionOpen)
    {
        var prevSessionDate = TradingCalendar.GetPreviousSessionDate(
            DateTime.ParseExact(sessionDate, "yyyyMMdd", null)).ToString("yyyyMMdd");

        var prevMinuteBars = await _barRepo.GetBarsBySessionDateAsync(prevSessionDate);
        if (prevMinuteBars.Count == 0) return null;

        var sequence = await loadRecentDailyBars(prevSessionDate, 40);
        if (sequence.Count < 21) return null;

        var j = sequence.Count - 1;
        var current = sequence[j];
        var atr5  = Computations.ATR(sequence, j, 5);
        var atr10 = Computations.ATR(sequence, j, 10);
        var atr20 = Computations.ATR(sequence, j, 20);
        var sma5  = Computations.SMA(sequence, j, 5, x => x.Close);
        var sma10 = Computations.SMA(sequence, j, 10, x => x.Close);
        var sma20 = Computations.SMA(sequence, j, 20, x => x.Close);
        var avgRange5  = Computations.AverageRange(sequence, j,  5);
        var avgRange10 = Computations.AverageRange(sequence, j, 10);
        var rsi14 = Computations.RSI(sequence, j, 14);
        var today = DateTime.ParseExact(sessionDate, "yyyyMMdd", null);

        double[] shared =
        [
            Operations.SafeDiv(current.High, sessionOpen),
            Operations.SafeDiv(current.Low, sessionOpen),
            Computations.HighLowRange(current, atr20),
            Computations.BodySize(current, atr20),
            Computations.UpperWick(current, atr20),
            Computations.LowerWick(current, atr20),
            Computations.ClosePosition(current),
            Operations.SafeDiv(atr5, sessionOpen),
            Operations.SafeDiv(atr10, sessionOpen),
            Operations.SafeDiv(atr20, sessionOpen),
            avgRange5,
            avgRange10,
            Operations.SafeDiv(current.High - current.Low, avgRange10),
            Computations.Return(sequence, j, 1),
            Computations.Return(sequence, j, 3),
            Computations.Return(sequence, j, 5),
            Operations.SafeDiv(sma5, sma20),
            Operations.SafeDiv(sma10, sma20),
            Operations.SafeDiv(sessionOpen, sma5),
            Operations.SafeDiv(sessionOpen, sma10),
            Operations.SafeDiv(sessionOpen, sma20),
            Computations.SMA(sequence, j, 20, x => x.Volume),
            Computations.VolumeZScore(sequence, j, 20),
            Operations.SafeDiv(sessionOpen - current.High, atr20),
            Operations.SafeDiv(sessionOpen - current.Low, atr20),
            Operations.SafeDiv(sessionOpen - current.Close, atr20),
            Operations.SafeDiv(atr10, atr20),
            rsi14,
            (int)today.DayOfWeek,
            today.Day,
        ];

        double[] tail =
        [
            Computations.OvernightRange(prevMinuteBars, atr20),
            Computations.OvernightVolumeRatio(prevMinuteBars),
            Computations.RegularHoursRange(prevMinuteBars, atr20),
            Computations.FirstHourRange(prevMinuteBars, atr20),
            Computations.LastHourRange(prevMinuteBars, atr20),
            Computations.HighTimeRatio(prevMinuteBars),
            Computations.PrevOpenToHighRatio(prevMinuteBars, atr20),
            Computations.PrevOpenToLowRatio(prevMinuteBars, atr20),
        ];

        var scaled = _highEval.Scale([.. shared, .. tail]);
        var predictedHigh = sessionOpen + _highEval.RunInference(scaled) * atr20;

        return new BarHighPrediction(predictedHigh, predictedMove: predictedHigh - sessionOpen);
    }

    private async Task<List<BarData>> loadRecentDailyBars(string sessionDate, int lookbackDays)
    {
        var list = new List<BarData>();
        var date = DateTime.ParseExact(sessionDate, "yyyyMMdd", null);

        for (var i = lookbackDays; i >= 0; i--)
        {
            var d = date.AddDays(-i).ToString("yyyyMMdd");
            var bars = await _barRepo.GetBarsBySessionDateAsync(d);
            if (bars.Count == 0) continue;
            list.Add(BarData.Aggregate(bars));
        }

        return list;
    }

    public void Dispose() => _highEval.Dispose();
}
