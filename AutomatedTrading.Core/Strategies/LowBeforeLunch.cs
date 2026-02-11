using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Signals;
using System.Collections.Generic;

namespace AutomatedTrading.Core.Strategies;

/// <summary>
/// Strategy to enter the market when the low hits in the trade window.
/// </summary>
public class LowBeforeLunch : TrailingStopStrategy
{    
    private IMarketState marketState;

    /// <summary>
    /// Trade window start.
    /// </summary>
    public int TradeWindowStart { get; private set; }
    
    /// <summary>
    /// Trade window end.
    /// </summary>
    public int TradeWindowEnd { get; private set; }

    /// <summary>
    /// Default constructor.
    /// </summary>
    /// <param name="tradeWindowStart"></param>
    /// <param name="tradeWindowEnd"></param>
    /// <param name="initialStopLoss"></param>
    /// <param name="trailTrigger"></param>
    /// <param name="trailDistance"></param>
    public LowBeforeLunch(int tradeWindowStart, int tradeWindowEnd, int initialStopLoss, int trailTrigger, int trailDistance)
    : base(initialStopLoss, trailTrigger, trailDistance)
    {
        TradeWindowStart = tradeWindowStart;
        TradeWindowEnd = tradeWindowEnd;
        marketState = new MarketState();
    }

    /// <summary>
    /// Sets defaults.
    /// </summary>
    /// <param name="bar"></param>
    public void Initialize(Bar bar)
    {
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
        return new InTradeWindow(TradeWindowStart, TradeWindowEnd).IsTriggered(bars) &&
            new IsSessionLow(marketState.DayLow.Low).IsTriggered(bars);
    }
}