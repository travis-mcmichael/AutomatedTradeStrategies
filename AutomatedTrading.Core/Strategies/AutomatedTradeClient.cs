using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.Enums;
using AutomatedTrading.Core.Framework.Logging;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Strategies;

public class AutomatedTradeClient
{
    private const string _url = "http://localhost:5244";
    public static string ENTRY_LONG  = "ATClient_Long";
    public static string ENTRY_SHORT = "ATClient_Short";
    private readonly List<NtTradeRecord> _trades = new List<NtTradeRecord>();
    private string _strategyId;
    private HttpClient _http;
    private string? _currentSessionDate;
    private double _proximity;
    private double _stopLoss;
    private double _targetProfit;
    private double _trail;
    private PositionSide _position;
    private bool _inTrade;
    private PositionSide _entryPosition;

    private double? _entryPrice = null;
    private string? _entryDate = null;
    private string? _entryTime = null;
    private double _profit;
    private double _loss;
    private int _winningTrades;
    private int _losingTrades;

    // Set by NinjaTrader on each order fill to keep the client in sync.
    public PositionSide Position   { get => _position;   set => _position = value; }
    public double?      EntryPrice { get => _entryPrice; set => _entryPrice = value; }

    public AutomatedTradeClient(string strategyId, double proximity, double stopLoss, double targetProfit, double trail)
    {
        _strategyId   = strategyId;
        _proximity    = proximity;
        _stopLoss     = stopLoss;
        _targetProfit = targetProfit;
        _trail        = trail;

        _position     = PositionSide.Flat;
        _profit       = 0.0;
        _loss         = 0.0;
        _winningTrades = 0;
        _losingTrades  = 0;
        _inTrade       = false;

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task StartSession(DateTime barTime, double sessionOpen)
    {
        var sessionDate = SessionDateFor(barTime).ToString("yyyyMMdd");
        _currentSessionDate = sessionDate;

        var request = new StrategySessionRequest(
            SessionDate:    sessionDate,
            SessionOpen:    sessionOpen,
            Proximity:      _proximity,
            StopLossPoints: _stopLoss,
            TargetProfit:   _targetProfit,
            Trail:          _trail);

        var body = JsonConvert.SerializeObject(request);
        try
        {
            var content  = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{_url}/strategy/session", content);
            if (!response.IsSuccessStatusCode)
            {
                Logger.Info($"Session endpoint returned {(int)response.StatusCode}:{response.ReasonPhrase}");
                return;
            }
            var json   = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<StrategySessionResponse>(json);
            if (result?.IsReady == true)
            {
                Logger.Info($"Session {sessionDate}: dailyATR={result.DailyAtr:F2}  open={sessionOpen:F2}  predictions={result.Predictions?.Count ?? 0}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Session call failed - {ex.Message}");
        }
    }

    public async Task<StrategyBarResponse?> Evaluate(BarData bar)
    {
        var request = new StrategyBarRequest(
            High:       bar.High,
            Low:        bar.Low,
            Close:      bar.Close,
            Position:   _position.ToString(),
            EntryPrice: _entryPrice ?? 0);

        var body = JsonConvert.SerializeObject(request);
        try
        {
            var content  = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{_url}/strategy/bar", content);
            if (!response.IsSuccessStatusCode) return null;
            var json        = await response.Content.ReadAsStringAsync();
            var barResponse = JsonConvert.DeserializeObject<StrategyBarResponse>(json);
            if (barResponse?.Signal == "EnterLong")
                _position = PositionSide.Long;
            else if (barResponse?.Signal == "EnterShort")
                _position = PositionSide.Short;
            return barResponse;
        }
        catch (Exception ex)
        {
            Logger.Error($"Bar eval failed - {ex.Message}");
        }

        return null;
    }

    public void OrderUpdate(DateTime time, string reason, double price)
    {
        // Entering a position
        if (_position != PositionSide.Flat && !_inTrade)
        {
            _inTrade       = true;
            _entryPosition = _position;
            _entryTime     = time.ToString(AppConstants.TIME_FORMAT);
            _entryDate     = time.ToString(AppConstants.DATE_FORMAT);
        }
        // Exiting a position
        else if (_position == PositionSide.Flat && _inTrade)
        {
            _inTrade = false;
            var pnl  = _entryPosition == PositionSide.Short
                ? (_entryPrice ?? price) - price
                : price - (_entryPrice ?? price);
            var trade = new NtTradeRecord
            {
                SessionDate = _currentSessionDate,
                EntryTime   = DateTime.ParseExact(
                    $"{_entryDate} {_entryTime}",
                    $"{AppConstants.DATE_FORMAT} {AppConstants.TIME_FORMAT}",
                    null),
                ExitTime    = time,
                Side        = _entryPosition == PositionSide.Short ? "Short" : "Long",
                EntryPrice  = _entryPrice ?? 0.0,
                ExitPrice   = price,
                PnL         = pnl,
                ExitReason  = reason
            };
            _trades.Add(trade);
            if (pnl > 0) { _profit += pnl;            _winningTrades++; }
            else          { _loss   += Math.Abs(pnl);  _losingTrades++;  }
        }
    }

    public void Terminate()
    {
        _http?.Dispose();
    }

    public class NtTradeRecord
    {
        public string?  SessionDate;
        public DateTime EntryTime;
        public DateTime ExitTime;
        public string   Side       = string.Empty;
        public double   EntryPrice;
        public double   ExitPrice;
        public double   PnL;
        public string   ExitReason = string.Empty;
    }

    public static DateTime SessionDateFor(DateTime barTime)
    {
        if (barTime.Hour <= 16) return barTime.Date;
        return TradingCalendar.GetNextSessionDate(barTime.Date);
    }
}
