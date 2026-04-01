namespace AutomatedTrading.Core.Api.Models.API.Predict;
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