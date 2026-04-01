namespace AutomatedTrading.Core.Api.Models;

public record SessionLowSessionRequest(
    string SessionDate,
    double SessionOpen,
    double Proximity = 0.5,
    double StopLossPoints = 5.0,
    double TargetProfit = 20.0,
    double Trail = 10.0
);

public record SessionLowSessionResponse(bool IsReady, double PredictedLow, double DailyAtr);

public record SessionLowBarRequest(double Low, double Close, string Position, double EntryPrice);

public record SessionLowBarResponse(string Signal, double StopPrice);