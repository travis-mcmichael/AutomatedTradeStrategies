namespace AutomatedTrading.Core.Api.Models.API.Predict;
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