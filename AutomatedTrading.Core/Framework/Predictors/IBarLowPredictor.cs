using System.Threading.Tasks;

namespace AutomatedTrading.Core.Framework.Predictors;
public interface IBarLowPredictor
{
    Task<BarLowPrediction?> PredictSessionLowAsync(string sessionDate, double sessionOpen);
}

public record BarLowPrediction
{
    public double PredictedLow { get; set; }
    public double PredictedMove { get; set; }
    public BarLowPrediction(double predictedLow, double predictedMove)
    {
        PredictedLow = predictedLow;
        PredictedMove = predictedMove;
    }
}