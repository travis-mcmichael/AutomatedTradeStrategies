using System.Threading.Tasks;

namespace AutomatedTrading.Core.Framework.Predictors;

public interface ITradeDayHighPredictor
{
    Task<TradeDayHighPrediction?> PredictTradeDayHighAsync(string sessionDate, double rthOpen);
}

public record TradeDayHighPrediction
{
    public double PredictedHigh { get; set; }
    public double PredictedMove { get; set; }
    public TradeDayHighPrediction(double predictedHigh, double predictedMove)
    {
        PredictedHigh = predictedHigh;
        PredictedMove = predictedMove;
    }
}
