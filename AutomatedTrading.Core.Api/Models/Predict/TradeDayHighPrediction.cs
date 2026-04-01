namespace AutomatedTrading.Core.Api.Models.API.Predict;
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