using System.Threading.Tasks;

namespace AutomatedTrading.Core.Framework.Predictors;

public interface ITradeDayLowPredictor
{
    Task<TradeDayLowPrediction?> PredictTradeDayLowAsync(string sessionDate, double rthOpen);
}

public record TradeDayLowPrediction
{
    public double PredictedLow  { get; set; }
    public double PredictedMove { get; set; }
    public TradeDayLowPrediction(double predictedLow, double predictedMove)
    {
        PredictedLow  = predictedLow;
        PredictedMove = predictedMove;
    }
}
