using System.Threading.Tasks;

namespace AutomatedTrading.Core.Strategies;

public interface IBarHighPredictor
{
    Task<BarHighPrediction?> PredictAsync(string sessionDate, double sessionOpen);
}

public record BarHighPrediction
{
    public double PredictedHigh {get;set;}
    public double PredictedMove {get;set;}
    public BarHighPrediction(double predictedHigh, double predictedMove)
    {
        PredictedHigh = predictedHigh;
        PredictedMove = predictedMove;
    }
}
