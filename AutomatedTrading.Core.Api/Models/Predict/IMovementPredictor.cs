namespace AutomatedTrading.Core.Api.Models.API.Predict;
public interface IMovementPredictor
{
    Task<MovementPrediction?> PredictMovement(string sessionDate, double[] features);
}