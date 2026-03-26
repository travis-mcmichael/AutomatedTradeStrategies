namespace AutomatedTrading.Core.Api.Models;

public record PredictedHighSessionRequest(
    string SessionDate,
    double SessionOpen,
    double Proximity = 0.5,
    double StopLossPoints = 5.0,
    double TargetProfit = 20.0,
    double Trail = 10.0
);

public record PredictedHighSessionResponse(bool IsReady, double PredictedHigh, double DailyAtr);

public record PredictedHighBarRequest(double High, double Close, string Position, double EntryPrice);

public record PredictedHighBarResponse(string Signal, double StopPrice);
