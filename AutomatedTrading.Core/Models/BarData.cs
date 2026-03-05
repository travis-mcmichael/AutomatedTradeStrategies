using System;
using AutomatedTrading.Core.Framework;

namespace AutomatedTrading.Core.Models;

public class BarData : Bar, IBarModel
{

    public string Instrument { get; set; }
    public string Timeframe { get; set; }
    public string SessionDate { get; set; }

    // Core Microstructure
    public double BidVolume { get; set; }
    public double AskVolume { get; set; }
    public double Delta { get; set; }
    public double CumulativeDelta { get; set; }
    public double VWAP { get; set; }
    
    // Volatility
    public double TrueRange { get; set; }
    public double ATR_14 { get; set; }
    public double RealizedVol_20 { get; set; }

    // Momentum
    public double RSI_14 { get; set; }
    public double RSI_7 { get; set; }
    public double MACD { get; set; }
    public double MACDSignal { get; set; }
    public double MACDHist { get; set; }
    public double Momentum_10 { get; set; }

    // Trend
    public double EMA_9 { get; set; }
    public double EMA_21 { get; set; }
    public double EMASpread { get; set; }
    public double ADX_14 { get; set; }

    // Mean Reversion
    public double BB_Upper { get; set; }
    public double BB_Lower { get; set; }
    public double BB_Width { get; set; }
    public double PercentB { get; set; }
    
    // Structure
    public double HighLowRange { get; set; }
    public double BodySize { get; set; }
    public double UpperWick { get; set; }
    public double LowerWick { get; set; }
    public double ClosePosition { get; set; }

    // Returns
    public double Return_1 { get; set; }
    public double Return_3 { get; set; }
    public double Return_5 { get; set; }
    public double Return_10 { get; set; }

    // Session Context
    public double MinutesFromOpen { get; set; }
    public int IsOpening { get; set; }
    public int IsMidDay { get; set; }
    public int IsClosing { get; set; }

    // Future Labels
    public double FutureReturn_1 { get; set; }
    public double FutureReturn_3 { get; set; }
    public double FutureReturn_5 { get; set; }
    public double FutureReturn_10 { get; set; }

    public int Label_Up_1 { get; set; }
    public int Label_Up_3 { get; set; }
    public int Label_Up_5 { get; set; }
    public int Label_Up_10 { get; set; }
}