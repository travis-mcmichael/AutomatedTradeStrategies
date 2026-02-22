using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Strategies;

/// <summary>
/// Class to keep all logic related to maintain a trailing stop strategy.
/// </summary>
public class TrailingStopStrategy : IHasLog
{
    /// <summary>
    /// Starting stop-loss points.
    /// </summary>
    public int InitialStopLoss { get; private set; }
    
    /// <summary>
    /// Amount to trigger updating stop-loss to trail current price.
    /// </summary>
    public int TrailTrigger { get; private set; }
    
    /// <summary>
    /// Amount relative to current value to update stop-loss.
    /// </summary>
    public int TrailDistance { get; private set; }
    
    /// <summary>
    /// Current stop-loss value.
    /// </summary>
    public double CurrentStopLoss { get; set; }
    
    /// <summary>
    /// Highest price since buying in.
    /// </summary>
    public double HighestPrice { get; set; } = 0;

    public string Log {get; protected set;} = "";

    /// <summary>
    /// Default constructor.
    /// </summary>
    /// <param name="initialStopLoss"></param>
    /// <param name="trailTrigger"></param>
    /// <param name="trailDistance"></param>
    public TrailingStopStrategy(int initialStopLoss, int trailTrigger, int trailDistance)
    {
        InitialStopLoss = initialStopLoss;
        TrailTrigger = trailTrigger;
        TrailDistance = trailDistance;
        CurrentStopLoss = 0;
    }

    public virtual void Initialize(Bar bar)
    {
        CurrentStopLoss = 0;
    }

    /// <summary>
    /// Sets stop-loss value if not already set.  Updates stop-loss if value has increased the requisite amount.
    /// </summary>
    /// <param name="bar"></param>
    /// <param name="buyInPrice"></param>
    /// <returns></returns>
    public bool EvaluateStopLoss(Bar bar)
    {
        HighestPrice = System.Math.Max(HighestPrice, bar.Close);
        if (CurrentStopLoss == 0)
        {
            CurrentStopLoss = HighestPrice - InitialStopLoss;
            
            Log = $"TrailingStop.EvaluateStopLoss - result: TRUE, CurrentStopLoss: {CurrentStopLoss:#.##}, HighestPrice: {HighestPrice:#.##}, InitialStop: {InitialStopLoss:#.##}";
            return true;
        }

        var profitPoints = HighestPrice - CurrentStopLoss;
        if (profitPoints >= TrailTrigger)
        {
            var newStop = HighestPrice - TrailDistance;
            if (newStop > CurrentStopLoss)
            {
                CurrentStopLoss = newStop;
                Log = $"TrailingStop.EvaluateStopLoss - result: TRUE, CurrentStopLoss: {CurrentStopLoss:#.##}, HighestPrice: {HighestPrice:#.##}, TrailDistance: {TrailDistance:#.##}";
                return true;
            }
        }

        Log = $"TrailingStop.EvaluateStopLoss - result: FALSE, CurrentStopLoss: {CurrentStopLoss:#.##}, HighestPrice: {HighestPrice:#.##}";
        return false;
    }
}