using AutomatedTrading.Core.Strategies;
using AutomatedTrading.Core.Framework.Enums;

namespace AutomatedTrading.Console.Backtesting;

/// <summary>Immutable record of a single completed trade.</summary>
public record TradeResult(
    string SessionDate,
    DateTime EntryTime,
    DateTime ExitTime,
    PositionSide Side,
    double EntryPrice,
    double ExitPrice,
    double PnL,
    string ExitReason
);