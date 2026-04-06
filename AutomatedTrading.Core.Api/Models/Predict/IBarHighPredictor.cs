namespace AutomatedTrading.Core.Api.Models.API.Predict;
public interface IBarHighPredictor
{
    Task<BarHighPrediction?> PredictSessionHighAsync(string sessionDate, double sessionOpen);
}