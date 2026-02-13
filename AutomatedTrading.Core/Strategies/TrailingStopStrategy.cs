using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Strategies;

/// <summary>
/// Class to keep all logic related to maintain a trailing stop strategy.
/// </summary>
public class TrailingStopStrategy
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
        CurrentStopLoss = initialStopLoss;
    }

    public virtual void Initialize(Bar bar)
    {
        CurrentStopLoss = 0;
    }

    /// <summary>
    /// Sets stop-loss value if not already set.  Updates stop-loss if value has increased the requisite amount.
    /// </summary>
    /// <param name="bar"></param>
    /// <param name="positionAveragePrice"></param>
    /// <returns></returns>
    public bool EvaluateStopLoss(Bar bar, double positionAveragePrice)
    {
        HighestPrice = System.Math.Max(HighestPrice, bar.Close);
        if (CurrentStopLoss == 0)
        {
            CurrentStopLoss = InitialStopLoss;
            return true;
        }

        var profitPoints = HighestPrice - positionAveragePrice;
        if (profitPoints >= TrailTrigger)
        {
            var newStop = HighestPrice - TrailDistance;
            if (newStop > CurrentStopLoss)
            {
                CurrentStopLoss = newStop;
                return true;
            }
        }

        return false;
    }
}