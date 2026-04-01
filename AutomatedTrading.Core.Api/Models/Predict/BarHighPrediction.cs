namespace AutomatedTrading.Core.Api.Models.API.Predict;

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