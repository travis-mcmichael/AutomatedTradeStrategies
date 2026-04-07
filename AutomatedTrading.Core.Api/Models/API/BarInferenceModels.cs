namespace AutomatedTrading.Core.Api.Models;

public record BarInferenceRequest(string SessionDate, double SessionOpen);

public record MovementResponse(double PredictedPrice, double PredictedMove);

