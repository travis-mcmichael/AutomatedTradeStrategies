namespace AutomatedTrading.Core.Models;

// Result of GetSessionOutcome: what the market actually did on a session, optionally restricted to a TradeWindow.
// Succeeded is false, with Error set, when the session has no usable bars.
public class SessionOutcome
{
    public bool Succeeded { get; set; }
    public string? Error { get; set; }
    public string SessionDate { get; set; } = "";
    // InstrumentVersion of the contract the session is taken from (after roll handling).
    public string? InstrumentVersion { get; set; }
    // The whole session.
    public SessionPriceRange? Session { get; set; }
    // The bars inside the window only. Null when no window was requested or no bars fell inside it.
    public string? WindowStart { get; set; }
    public string? WindowEnd { get; set; }
    public SessionPriceRange? Window { get; set; }

    public static SessionOutcome Failed(string sessionDate, string error) =>
        new() { SessionDate = sessionDate, Error = error };
}

// OHLCV over a set of bars, aggregated the way training and inference aggregate them (open = first bar's open).
public class SessionPriceRange
{
    public int BarCount { get; set; }
    // HH:mm:ss of the first and last bar included.
    public string FirstTime { get; set; } = "";
    public string LastTime { get; set; } = "";
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public double Volume { get; set; }
    // HH:mm:ss of the first bar that reached the high or low.
    public string HighTime { get; set; } = "";
    public string LowTime { get; set; } = "";
    // Points from the open to the high and from the open down to the low. These are the same quantities as the
    // HIGH and LOW training targets (before dividing by ATR), so they compare directly with InferencePrediction.Move.
    public double UpMove => High - Open;
    public double DownMove => Open - Low;
}
