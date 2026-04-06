namespace AutomatedTrading.Core.Api.Models.API.Predict;
public interface IBarLowPredictor
{
    Task<BarLowPrediction?> PredictSessionLowAsync(string sessionDate, double sessionOpen);
}