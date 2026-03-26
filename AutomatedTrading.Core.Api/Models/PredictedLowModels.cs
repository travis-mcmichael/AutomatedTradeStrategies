namespace AutomatedTrading.Core.Api.Models;

public record PredictedLowSessionRequest(
    string SessionDate,
    double SessionOpen,
    double Proximity = 0.5,
    double StopLossPoints = 5.0,
    double TargetProfit = 20.0,
    double Trail = 10.0
);

public record PredictedLowSessionResponse(bool IsReady, double PredictedLow, double DailyAtr);

public record PredictedLowBarRequest(double Low, double Close, string Position, double EntryPrice);

public record PredictedLowBarResponse(string Signal, double StopPrice);