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
    public double InitialStopLoss { get; private set; }

    /// <summary>
    /// Amount to trigger updating stop-loss to trail current price.
    /// </summary>
    public double TrailTrigger { get; private set; }

    /// <summary>
    /// Amount relative to current value to update stop-loss.
    /// </summary>
    public double TrailDistance { get; private set; }

    /// <summary>
    /// Current stop-loss value.
    /// </summary>
    public double CurrentStopLoss { get; set; }

    /// <summary>
    /// Highest price since buying in.
    /// </summary>
    public double HighestPrice { get; set; } = 0;

    public string Log { get; protected set; } = "";

    /// <summary>
    /// Default constructor.
    /// </summary>
    /// <param name="initialStopLoss"></param>
    /// <param name="trailTrigger"></param>
    /// <param name="trailDistance"></param>
    public TrailingStopStrategy(double initialStopLoss, double trailTrigger, double trailDistance)
    {
        InitialStopLoss = initialStopLoss;
        TrailTrigger = trailTrigger;
        TrailDistance = trailDistance;
        CurrentStopLoss = 0;
        Log = $"TrailingStop.ctor\r\n\tInitialStopLoss: {InitialStopLoss}\r\n\tTrailTrigger: {TrailTrigger}\r\n\tCurrentStopLoss: {CurrentStopLoss}";
    }

    public virtual void Initialize(BarData bar)
    {
        CurrentStopLoss = 0;
    }

    public void UpdateStopLoss(BarData bar)
    {
        CurrentStopLoss = bar.Close - InitialStopLoss;
        HighestPrice = bar.Close;
        Log = $"TrailingStop.UpdateStopLoss\r\n\tInitialStopLoss: {InitialStopLoss}\r\n\tHighestPrice: {HighestPrice}";
    }

    /// <summary>
    /// Sets stop-loss value if not already set.  Updates stop-loss if value has increased the requisite amount.
    /// </summary>
    /// <param name="bar"></param>
    /// <param name="buyInPrice"></param>
    /// <returns></returns>
    public bool EvaluateStopLoss(BarData bar)
    {
        var previous = HighestPrice;
        var current = System.Math.Max(HighestPrice, bar.Close);
        if (CurrentStopLoss == 0)
        {
            HighestPrice = current;
            CurrentStopLoss = current - InitialStopLoss;

            Log = $"TrailingStop.EvaluateStopLoss\r\n\tresult: \tTRUE\r\n\tCurrentStopLoss:\t{CurrentStopLoss:#.##}\r\n\tHighestPrice:\t{HighestPrice:#.##}\r\n\tInitialStop:\t{InitialStopLoss:#.##}";
            return true;
        }

        var profitPoints = current - previous;
        if (profitPoints >= TrailTrigger)
        {
            HighestPrice = current;

            var newStop = HighestPrice - TrailDistance;
            if (newStop > CurrentStopLoss)
            {
                CurrentStopLoss = newStop;
                Log = $"TrailingStop.EvaluateStopLoss\r\n\tresult: TRUE\r\n\tCurrentStopLoss:\t{CurrentStopLoss:#.##}\r\n\tTrailDistance:\t{TrailDistance:#.##}\r\n\tHighestPrice:\t{HighestPrice:#.##}\r\n\tprevious high:\t{previous:#.##}";
                return true;
            }
        }

        Log = $"TrailingStop.EvaluateStopLoss\r\n\tresult: FALSE\r\n\tCurrentStopLoss:\t{CurrentStopLoss:#.##}\r\n\tHighestPrice:\t{HighestPrice:#.##}";
        return false;
    }
}