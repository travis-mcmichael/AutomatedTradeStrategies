namespace AutomatedTrading.Core.Api.Models;

public record TradeDayHighSessionRequest(
    string SessionDate,
    double SessionOpen,
    double Proximity = 0.5,
    double StopLossPoints = 5.0,
    double TargetProfit = 20.0,
    double Trail = 10.0
);

public record TradeDayHighSessionResponse(bool IsReady, double PredictedHigh, double DailyAtr);

public record TradeDayHighBarRequest(double High, double Close, string Position, double EntryPrice);

public record TradeDayHighBarResponse(string Signal, double StopPrice);
