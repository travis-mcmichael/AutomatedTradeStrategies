using System;

namespace AutomatedTrading.Core.Framework;

public interface IBarModel
{
    int Time { get; set; }
    double Open { get; set; }
    double High { get; set; }
    double Low { get; set; }
    double Close { get; set; }
    double Volume { get; set; }
    DateTime? Timestamp { get; set; }
    string ToLog();
}