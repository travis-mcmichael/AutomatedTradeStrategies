namespace AutomatedTrading.Core.Api.Models;

public record StrategySessionRequest(
    string SessionDate,
    double SessionOpen,
    double Proximity = 0.5,
    double StopLossPoints = 5.0,
    double TargetProfit = 20.0,
    double Trail = 10.0
);

public record StrategySessionResponse(
    bool IsReady,
    IReadOnlyDictionary<string, double> Predictions,
    double DailyAtr
);

public record StrategyBarRequest(
    double High,
    double Low,
    double Close,
    string Position,
    double EntryPrice = 0
);

public record StrategyBarResponse(string Signal, double StopPrice);
