using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Api.Models.API.Predict;
public interface IMovementPredictor
{
    Task<MovementPrediction?> PredictMovement(List<BarData> sequence, Dictionary<string, List<BarData>> data);
}