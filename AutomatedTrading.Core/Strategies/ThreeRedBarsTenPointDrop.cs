using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Signals;
using System.Collections.Generic;
using System.Linq;

namespace AutomatedTrading.Core.Strategies;

public class ThreeRedBarsTenPointDrop : TrailingStopStrategy, IHasLog
{
    private IMarketState marketState;
    public string Log { get; private set; } =  "";
    public ThreeRedBarsTenPointDrop(int initialStopLoss, int trailTrigger, int trailDistance)
    : base(initialStopLoss, trailTrigger, trailDistance)
    {
        marketState = new MarketState();
    }

    public override void Initialize(Bar bar)
    {
        base.Initialize(bar);
        marketState.OnNewSession(bar);
    }

    public bool Evaluate(List<Bar> bars)
    {
        if (!bars.Any())
            return false;

        marketState.OnNewBar(bars.Last());
        var lastThreeBarsRed = new IsTrending(3, PriceDirection.DECREASING);
        var tenPointDrop = new BarChange(10, 3, PriceDirection.DECREASING);

        var lastThreeBarsRedSignal = lastThreeBarsRed.IsTriggered(bars);
        var tenPointDropSignal = tenPointDrop.IsTriggered(bars);

        Log = LogBuilder.GetEvaluationResultLog(
            bars.Last(),
            "ThreeRedBarsTenPointDrop.Evaluate",
            new Dictionary<string, object>
            {
                { "lastThreeBarsRed", lastThreeBarsRed.Log },
                { "tenPointDrop", tenPointDrop.Log }
            }
        );

        return lastThreeBarsRedSignal && tenPointDropSignal;
    }

    public string GetSessionSummaryLog()
    {
        return LogBuilder.GetSessionSummaryLog(marketState);
    }
}