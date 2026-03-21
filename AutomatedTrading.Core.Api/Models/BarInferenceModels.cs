namespace AutomatedTrading.Core.Api.Models;

public record BarInferenceRequest(string SessionDate, double SessionOpen);

public record BarHighResponse(double PredictedHigh, double PredictedMove);

public record BarLowResponse(double PredictedLow, double PredictedMove);
