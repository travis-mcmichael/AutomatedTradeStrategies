using AutomatedTrading.Core.Framework.Enums;
using AutomatedTrading.Core.Framework.Predictors;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Console.Backtesting;

/// <summary>
/// Abstracts any strategy evaluator for use in backtesting.
/// Implementations extract the correct bar value (High vs Low) and encode
/// which direction triggers the stop, so <see cref="BacktestRunner"/> stays
/// strategy-agnostic.
/// </summary>
public interface IBacktestEvaluator
{
    void StartSession(double prediction, double dailyAtr);
    BarEvaluation Evaluate(BarData bar, PositionSide position, double entryPrice);
    bool IsStopTriggered(BarData bar, PositionSide position, double stopPrice);
}

/// <summary>
/// Wraps a "high" strategy evaluator — predicts the session high and enters short.
/// Pass <c>evaluator.StartSession</c> and <c>evaluator.Evaluate</c> as the delegates.
/// <example>
/// <code>
///   var e = new SessionHighStrategyEvaluator(proximity: 0.5, ...);
///   new BacktestRun("name", new HighBacktestEvaluator(e.StartSession, e.Evaluate));
/// </code>
/// </example>
/// </summary>
public sealed class HighBacktestEvaluator : IBacktestEvaluator
{
    private readonly Action<double, double> _startSession;
    private readonly Func<double, double, PositionSide, double, BarEvaluation> _evaluate;

    public HighBacktestEvaluator(
        Action<double, double> startSession,
        Func<double, double, PositionSide, double, BarEvaluation> evaluate)
    {
        _startSession = startSession;
        _evaluate = evaluate;
    }

    public void StartSession(double prediction, double dailyAtr)
        => _startSession(prediction, dailyAtr);

    public BarEvaluation Evaluate(BarData bar, PositionSide position, double entryPrice)
        => _evaluate(bar.High, bar.Close, position, entryPrice);

    /// <summary>Stop is hit when a short position's bar.High reaches the stop price.</summary>
    public bool IsStopTriggered(BarData bar, PositionSide position, double stopPrice)
        => position == PositionSide.Short && bar.High >= stopPrice;
}

/// <summary>
/// Wraps a "low" strategy evaluator — predicts the session low and enters long.
/// Pass <c>evaluator.StartSession</c> and <c>evaluator.Evaluate</c> as the delegates.
/// <example>
/// <code>
///   var e = new SessionLowStrategyEvaluator(proximity: 0.5, ...);
///   new BacktestRun("name", new LowBacktestEvaluator(e.StartSession, e.Evaluate));
/// </code>
/// </example>
/// </summary>
public sealed class LowBacktestEvaluator : IBacktestEvaluator
{
    private readonly Action<double, double> _startSession;
    private readonly Func<double, double, PositionSide, double, BarEvaluation> _evaluate;

    public LowBacktestEvaluator(
        Action<double, double> startSession,
        Func<double, double, PositionSide, double, BarEvaluation> evaluate)
    {
        _startSession = startSession;
        _evaluate = evaluate;
    }

    public void StartSession(double prediction, double dailyAtr)
        => _startSession(prediction, dailyAtr);

    public BarEvaluation Evaluate(BarData bar, PositionSide position, double entryPrice)
        => _evaluate(bar.Low, bar.Close, position, entryPrice);

    /// <summary>Stop is hit when a long position's bar.Low falls to the stop price.</summary>
    public bool IsStopTriggered(BarData bar, PositionSide position, double stopPrice)
        => position == PositionSide.Long && bar.Low <= stopPrice;
}
