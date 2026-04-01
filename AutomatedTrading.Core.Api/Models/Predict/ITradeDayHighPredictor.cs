namespace AutomatedTrading.Core.Api.Models.API.Predict;
public interface ITradeDayHighPredictor
{
    Task<TradeDayHighPrediction?> PredictTradeDayHighAsync(string sessionDate, double rthOpen);
}
