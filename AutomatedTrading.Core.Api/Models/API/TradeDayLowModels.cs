namespace AutomatedTrading.Core.Api.Models;

public record TradeDayLowSessionRequest(
    string SessionDate,
    double SessionOpen,
    double Proximity = 0.5,
    double StopLossPoints = 5.0,
    double TargetProfit = 20.0,
    double Trail = 10.0
);

public record TradeDayLowSessionResponse(bool IsReady, double PredictedLow, double DailyAtr);

public record TradeDayLowBarRequest(double Low, double Close, string Position, double EntryPrice);

public record TradeDayLowBarResponse(string Signal, double StopPrice);
