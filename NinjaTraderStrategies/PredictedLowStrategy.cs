#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using Newtonsoft.Json;
using System.Globalization;

using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.Utility;
#endregion

#region README IMPORTANT

/*
    This strategy calls the AutomatedTrading.Core.Api at session open to retrieve
    the predicted LOW level for the session. It trades a long reversal near that
    level, and will flatten any open short if price reaches the predicted low.

    Designed for RTH 1-minute bars (8:30 AM – 3:15 PM CT). Ensure the API is
    running before the strategy is enabled.

    Stop loss is anchored to the predicted level, not the entry price. If price
    breaks through the predicted low by StopLossPoints, the prediction has
    failed and the position is closed with a bounded loss.

    Entries:
      Near predicted LOW → EnterLong (or ExitShort to flatten)

    Proximity and stop parameters are tunable via the strategy properties panel.
*/

#endregion

//This namespace holds Strategies in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Strategies
{
    public class PredictedLowStrategy : Strategy
    {
        // =====================================================================
        // Constants
        // =====================================================================
        private const string API_URL = "http://localhost:5244";
        private const string STRATEGY_ID = "PredictedLow";
        private const string ENTRY_LONG = "PL_Long";
        private const string ENTRY_SHORT = "PL_Short";

        // =====================================================================
        // Fields
        // =====================================================================
        private HttpClient _http;
        private bool _inferenceReady;
        private double _predictedLow;

        // ── Trade tracking ────────────────────────────────────────────────────
        private string _currentSessionDate;
        private bool _inTrade;
        private DateTime _entryTime;
        private double _entryPrice;
        private readonly List<NtTradeRecord> _trades = new List<NtTradeRecord>();
        private double _grossProfit;
        private double _grossLoss;
        private int _winningTrades;
        private int _losingTrades;

        // =====================================================================
        // Parameters
        // =====================================================================

        /// <summary>
        /// Multiplier applied to the 20-day ATR to determine how close price must
        /// get to the predicted high before triggering an entry or flatten.
        /// Effective proximity = Proximity x ATR(20 days). Smaller = tighter, fewer triggers.
        /// </summary>
        [NinjaScriptProperty]
        [Range(0.01, 10.0)]
        [Display(Name = "Proximity (ATR×20 mult.)", Order = 1, GroupName = "Levels")]
        public double Proximity { get; set; }

        /// <summary>
        /// How far (in points) beyond the predicted low the stop is placed.
        /// The stop is anchored to the predicted low, not the entry price,
        /// so maximum risk is ProximityPoints + StopLossPoints per contract.
        /// </summary>
        [NinjaScriptProperty]
        [Range(0.25, double.MaxValue)]
        [Display(Name = "Stop Loss Points", Order = 2, GroupName = "Levels")]
        public double StopLossPoints { get; set; }
        
        /// <summary>
        /// Profit in points that must be achieved before the trailing stop activates.
        /// Once entryPrice - currentPrice >= TargetProfit, the trail engages.
        /// </summary>
        [NinjaScriptProperty]
        [Range(0.25, double.MaxValue)]
        [Display(Name = "Target Profit (points)", Order = 3, GroupName = "Trailing Stop")]
        public double TargetProfit { get; set; }

        /// <summary>
        /// Number of points above the current bar's close at which the trailing stop
        /// is placed once TargetProfit has been reached. The stop only ratchets down
        /// — it never moves back up — locking in more profit as price falls further.
        /// </summary>
        [NinjaScriptProperty]
        [Range(0.25, double.MaxValue)]
        [Display(Name = "Trail (points)", Order = 4, GroupName = "Trailing Stop")]
        public double Trail { get; set; }

        // =====================================================================
        // Lifecycle
        // =====================================================================
        protected override void OnStateChange()
        {
            switch (State)
            {
                case State.SetDefaults:
                    Name = STRATEGY_ID;
                    Calculate = Calculate.OnBarClose;
                    EntriesPerDirection = 1;
                    IsExitOnSessionCloseStrategy = true;
                    ExitOnSessionCloseSeconds = 30;
                    BarsRequiredToTrade = 1;

                    Proximity = 0.5;
                    StopLossPoints  = 5.0;
                    TargetProfit = 20.0;
                    Trail = 10.0;

                    _inferenceReady = false;
                    _predictedLow = double.MinValue;
                    break;
                case State.Configure:
                    _http = new HttpClient{ Timeout = TimeSpan.FromSeconds(10) };
                break;
                case State.Terminated:
                    _http?.Dispose();
                    printSummary();
                break;
                default:
                    break;
            }
        }

        // =====================================================================
        // Bar update
        // =====================================================================
        protected override void OnBarUpdate()
        {
            if (Bars.IsFirstBarOfSession)
            {
                _inferenceReady = false;
                _predictedLow = double.MinValue;
                startSession(Time[0], Open[0]);
            }

            if (!_inferenceReady)
                return;

            var eval = evaluateBar(Low[0], Close[0]);
            if (eval is null) return;

            switch (eval.Signal)
            {
                case "EnterLong":
                    SetStopLoss(ENTRY_LONG, CalculationMode.Price, eval.StopPrice, false);
                    EnterLong(ENTRY_LONG);
                    Print($"{Time[0]:HH:mm} PL: EnterLong near predicted low ({_predictedLow:F2}), stop={eval.StopPrice:F2}");
                    break;
                case "ExitShort":
                    ExitShort(ENTRY_SHORT);
                    Print($"{Time[0]:HH:mm} PL: ExitShort — price reached predicted low ({_predictedLow:F2})");
                    break;
                case "UpdateStop":
                    SetStopLoss(ENTRY_LONG, CalculationMode.Price, eval.StopPrice, false);
                    Print($"{Time[0]:HH:mm} PL: Trailing stop updated to {eval.StopPrice:F2}");
                    break;
            }
        }

        // =====================================================================
        // API call
        // =====================================================================

        private static DateTime sessionDateFor(DateTime barTime)
        {
            if (barTime.Hour <= 16) return barTime.Date;
            return TradingCalendar.GetNextSessionDate(barTime.Date);
        }

        private void startSession(DateTime barTime, double sessionOpen)
        {
            var sessionDate = sessionDateFor(barTime).ToString("yyyyMMdd");
            _currentSessionDate = sessionDate;
            var body = JsonConvert.SerializeObject(new
            {
                sessionDate,
                sessionOpen,
                proximity = Proximity,
                stopLossPoints = StopLossPoints,
                targetProfit = TargetProfit,
                trail = Trail,
            });
            try
            {
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = _http.PostAsync($"{API_URL}/strategy/predicted-low/session", content)
                    .GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    Print($"PL: session endpoint returned {(int)response.StatusCode}");
                    return;
                }
                var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var result = JsonConvert.DeserializeObject<SessionApiResponse>(json);
                if (result?.IsReady == true)
                {
                    _inferenceReady = true;
                    _predictedLow = result.PredictedLow;
                    Print($"PL {sessionDate}: predictedLow={_predictedLow:F2}  dailyATR={result.DailyAtr:F2}  open={sessionOpen:F2}");
                }
            }
            catch (Exception ex)
            {
                Print($"PL: session call failed — {ex.Message}");
            }
        }
        
        private BarApiResponse evaluateBar(double low, double close)
        {
            var entryPrice = Position.MarketPosition == MarketPosition.Long
                ? Position.AveragePrice
                : 0.0;

            var body = JsonConvert.SerializeObject(new
            {
                low,
                close,
                position = positionString(),
                entryPrice,
            });

            try
            {
                var content  = new StringContent(body, Encoding.UTF8, "application/json");
                var response = _http.PostAsync($"{API_URL}/strategy/predicted-low/bar", content)
                    .GetAwaiter().GetResult();

                if (!response.IsSuccessStatusCode) return null;

                var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return JsonConvert.DeserializeObject<BarApiResponse>(json);
            }
            catch (Exception ex)
            {
                Print($"PL: bar eval call failed — {ex.Message}");
                return null;
            }
        }

        // =====================================================================
        // Trade tracking
        // =====================================================================
        protected override void OnExecutionUpdate(Execution execution, string executionId,
            double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (marketPosition == MarketPosition.Long && !_inTrade)
            {
                _inTrade    = true;
                _entryTime  = time;
                _entryPrice = price;
            }
            else if (marketPosition == MarketPosition.Flat && _inTrade)
            {
                _inTrade = false;
                var exitReason = execution.Order.OrderType == OrderType.StopMarket ? "Stop" : "SessionClose";
                var pnl        = price - _entryPrice;
                var trade      = new NtTradeRecord
                {
                    SessionDate = _currentSessionDate,
                    EntryTime   = _entryTime,
                    ExitTime    = time,
                    Side        = "Long",
                    EntryPrice  = _entryPrice,
                    ExitPrice   = price,
                    PnL         = pnl,
                    ExitReason  = exitReason,
                };
                _trades.Add(trade);
                if (pnl > 0) { _grossProfit += pnl; _winningTrades++; }
                else          { _grossLoss   -= pnl; _losingTrades++;  }
            }
        }

        private void printSummary()
        {
            if (_trades.Count == 0) return;
            var net = _grossProfit - _grossLoss;
            Print($"\n{"Run",-30} {"Trades",6} {"Wins",5} {"Losses",6} {"GrossP",9} {"GrossL",9} {"Net",9}");
            Print(new string('-', 80));
            Print($"{Name,-30} {_trades.Count,6} {_winningTrades,5} {_losingTrades,6}" +
                  $" {_grossProfit,9:F2} {_grossLoss,9:F2} {net,9:F2}");
            var counter = 0;
            foreach (var trade in _trades)
            {
                var dollars = trade.PnL * AppConstants.DOLLARS_PER_POINT;
                Print($"\tTrade # {++counter}, Session:{trade.SessionDate}, Pos:{trade.Side}," +
                      $" profit (pts):{trade.PnL:F2} - ${dollars:F2}");
                Print($"\t\tEntryTime:\t{trade.EntryTime:HHmmss}\t{"Entry",8}:{trade.EntryPrice,10:F2}");
                Print($"\t\tExitTime:\t{trade.ExitTime:HHmmss}\t{"Exit",8}:{trade.ExitPrice,10:F2}\tReason:{trade.ExitReason,10}");
            }
        }

        private string positionString() => Position.MarketPosition switch
        {
            MarketPosition.Long => "Long",
            MarketPosition.Short => "Short",
            _ => "Flat"
        };

        // =====================================================================
        // Inner types
        // =====================================================================
        private class NtTradeRecord
        {
            public string   SessionDate;
            public DateTime EntryTime;
            public DateTime ExitTime;
            public string   Side;
            public double   EntryPrice;
            public double   ExitPrice;
            public double   PnL;
            public string   ExitReason;
        }

        // =====================================================================
        // API response POCOs
        // =====================================================================
        private class SessionApiResponse
        {
            [JsonProperty("isReady")]
            public bool IsReady { get; set; }

            [JsonProperty("predictedLow")]
            public double PredictedLow { get; set; }

            [JsonProperty("dailyAtr")]
            public double DailyAtr { get; set; }
        }

        private class BarApiResponse
        {
            [JsonProperty("signal")]
            public string Signal { get; set; }

            [JsonProperty("stopPrice")]
            public double StopPrice { get; set; }
        }
    }
}