using System.Threading.Tasks;
namespace AutomatedTrading.Core.Api.Models.API.Predict;
public interface ITradeDayLowPredictor
{
    Task<TradeDayLowPrediction?> PredictTradeDayLowAsync(string sessionDate, double rthOpen);
}