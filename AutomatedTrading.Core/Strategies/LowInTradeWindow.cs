using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Signals;
using System.Collections.Generic;

namespace AutomatedTrading.Core.Strategies;

/// <summary>
/// Strategy to enter the market when the low hits in the trade window.
/// </summary>
public class LowInTradeWindow : TrailingStopStrategy, IHasLog
{
    private IMarketState marketState;
    private bool firstBarOfTradeWindow = true;

    /// <summary>
    /// Trade window start.
    /// </summary>
    public int TradeWindowStart { get; private set; }

    /// <summary>
    /// Trade window end.
    /// </summary>
    public int TradeWindowEnd { get; private set; }

    /// <summary>
    /// Log.
    /// </summary>
    public string Log { get; private set; } = "";

    /// <summary>
    /// Default constructor.
    /// </summary>
    /// <param name="tradeWindowStart"></param>
    /// <param name="tradeWindowEnd"></param>
    /// <param name="initialStopLoss"></param>
    /// <param name="trailTrigger"></param>
    /// <param name="trailDistance"></param>
    public LowInTradeWindow(int tradeWindowStart, int tradeWindowEnd, int initialStopLoss, int trailTrigger, int trailDistance, double positionAverage)
    : base(initialStopLoss, trailTrigger, trailDistance, positionAverage)
    {
        TradeWindowStart = tradeWindowStart;
        TradeWindowEnd = tradeWindowEnd;
        marketState = new MarketState();
    }

    /// <summary>
    /// Sets defaults.
    /// </summary>
    /// <param name="bar"></param>
    public override void Initialize(Bar bar)
    {
        base.Initialize(bar);
        marketState.OnNewSession(bar);
    }

    /// <summary>
    /// Evaluates the current bar against the market state to decide whether to enter market.
    /// </summary>
    /// <param name="bar"></param>
    /// <returns></returns>
    public bool Evaluate(Bar bar)
    {
        marketState.OnNewBar(bar);
        var bars = new List<Bar> { bar };

        var inTradeWindow = new InTradeWindow(TradeWindowStart, TradeWindowEnd);
        var isSessionLow = new IsSessionLow(marketState.DayLow?.Time ?? 0, marketState.DayLow?.Close ?? 0L, marketState.BarCount);

        var inTradeWindowSignalled = inTradeWindow.IsTriggered(bars);
        var isSessionLowSignalled = isSessionLow.IsTriggered(bars);

        Log = LogBuilder.GetEvaluationResultLog(
            bar,
            "LowInTradeWindow.Evaluate",
            new Dictionary<string, object>
            {
                { "inTradeWindow", inTradeWindow.Log },
                { "isSessionLow", isSessionLow.Log }
            });

        if (inTradeWindowSignalled && firstBarOfTradeWindow)
        {
            firstBarOfTradeWindow = false;
            marketState.OnNewSession(bar);
        }

        return inTradeWindowSignalled && isSessionLowSignalled;
    }
}