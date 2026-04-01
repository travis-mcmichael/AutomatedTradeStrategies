namespace AutomatedTrading.Core.Api.Models;

public record SessionHighSessionRequest(
    string SessionDate,
    double SessionOpen,
    double Proximity = 0.5,
    double StopLossPoints = 5.0,
    double TargetProfit = 20.0,
    double Trail = 10.0
);

public record SessionHighSessionResponse(bool IsReady, double PredictedHigh, double DailyAtr);

public record SessionHighBarRequest(double High, double Close, string Position, double EntryPrice);

public record SessionHighBarResponse(string Signal, double StopPrice);
