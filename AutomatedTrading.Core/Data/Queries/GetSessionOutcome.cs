using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Utility;

namespace AutomatedTrading.Core.Data.Queries;

// The actual price action of a session date, to compare with an InferencePrediction for the same date.
// Uses the same contract handling as GetInferencePrediction: the session's bars come from the contract that is active
// on that date, back-adjusted only by rolls on or before it, so the prices are on the same basis as the prediction.
// Optionally restricted to a TradeWindow, aggregated as training does (the window's open is its first bar's open).
public class GetSessionOutcome(
    GetVersionChangeovers versionChangeovers,
    GetBarsInSessionRange barsInRange)
{
    // Sessions back from the session date that are scanned for contract rolls. Only the roll history matters here;
    // the session's own bars are the only ones loaded.
    private const int CHANGEOVER_LOOKBACK_SESSIONS = 20;
    private const string TIME_FORMAT = "HH:mm:ss";

    public async Task<SessionOutcome> Execute(string instrument, DateTime sessionDate, TradeWindow? window = null)
    {
        var label = sessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var sessionKey = Key(sessionDate);

        if (!TradingCalendar.IsTradingDay(sessionKey))
            return SessionOutcome.Failed(label, $"{label} is not a trading session.");

        try
        {
            var bars = await barsInRange.Execute(instrument, sessionKey, sessionKey);
            if (bars.Count == 0)
                return SessionOutcome.Failed(label, $"There are no bars for {label}.");

            // Same roll handling as GetInferencePrediction. The range ends on this session, so no later roll
            // shifts its prices.
            var earliest = sessionDate.Date;
            for (var i = 0; i < CHANGEOVER_LOOKBACK_SESSIONS; i++)
                earliest = TradingCalendar.GetPreviousSessionDate(earliest);

            var changeovers = await versionChangeovers.Execute(instrument, earliest, sessionDate);
            var bySession = new Dictionary<string, List<BarData>> { [sessionKey] = bars };
            if (changeovers.Count > 0)
                FeatureExporter.HandleChangeoverAdjustment(changeovers, new List<string> { sessionKey }, bySession);

            if (!bySession.TryGetValue(sessionKey, out var sessionBars) || sessionBars.Count == 0)
                return SessionOutcome.Failed(label, $"No bars for {label} belong to the active contract.");

            sessionBars = [.. sessionBars.OrderBy(b => b.Timestamp)];
            var outcome = new SessionOutcome
            {
                Succeeded = true,
                SessionDate = label,
                InstrumentVersion = sessionBars[0].InstrumentVersion,
                Session = summarize(sessionBars),
            };

            if (window is not null)
            {
                outcome.WindowStart = FormatTime(window.WindowStart);
                outcome.WindowEnd = FormatTime(window.WindowEnd);
                var inWindow = sessionBars.Where(b => window.InWindow(timeOf(b))).ToList();
                if (inWindow.Count > 0)
                    outcome.Window = summarize(inWindow);
            }

            return outcome;
        }
        catch (Exception ex)
        {
            return SessionOutcome.Failed(label, ex.Message);
        }
    }

    // Aggregates in time order: open is the first bar's open, close the last bar's close.
    private static SessionPriceRange summarize(List<BarData> bars)
    {
        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        var high = ordered.Max(b => b.High);
        var low = ordered.Min(b => b.Low);
        return new SessionPriceRange
        {
            BarCount = ordered.Count,
            FirstTime = FormatTime(timeOf(ordered[0])),
            LastTime = FormatTime(timeOf(ordered[^1])),
            Open = ordered[0].Open,
            Close = ordered[^1].Close,
            High = high,
            Low = low,
            Volume = ordered.Sum(b => b.Volume),
            HighTime = FormatTime(timeOf(ordered.First(b => b.High == high))),
            LowTime = FormatTime(timeOf(ordered.First(b => b.Low == low))),
        };
    }

    private static int timeOf(BarData bar) =>
        int.TryParse(bar.SessionTime, NumberStyles.None, CultureInfo.InvariantCulture, out var t) ? t : -1;

    private static string FormatTime(int hhmmss)
    {
        var s = hhmmss.ToString("000000", CultureInfo.InvariantCulture);
        return $"{s.Substring(0, 2)}:{s.Substring(2, 2)}:{s.Substring(4, 2)}";
    }

    private static string Key(DateTime date) => date.ToString(AppConstants.DATE_FORMAT, CultureInfo.InvariantCulture);
}
