using System;

namespace AutomatedTrading.Core.Framework;

/// <summary>
/// Converts between stored timestamps (UTC epoch milliseconds) and exchange time (US Central), which is what
/// everything that reads <c>BarData.Timestamp</c> expects: the session windows in <see cref="Computations"/>,
/// the trade-window signals and the times in <see cref="AppConstants"/>. The zone is explicit rather than the
/// machine's local zone, so results are the same on a Central-time workstation and a UTC Linux server.
/// </summary>
public static class MarketTime
{
    public static readonly TimeZoneInfo Central = findCentral();

    /// <summary>Central wall-clock time (DateTimeKind.Unspecified) for a UTC epoch-milliseconds value.</summary>
    public static DateTime FromUnixMilliseconds(long milliseconds) =>
        FromUtc(DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime);

    /// <summary>Central wall-clock time (DateTimeKind.Unspecified) for a UTC time.</summary>
    public static DateTime FromUtc(DateTime utc) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Central), DateTimeKind.Unspecified);

    /// <summary>
    /// UTC epoch milliseconds for a time. Utc and Local kinds are converted as such; Unspecified (what
    /// <see cref="FromUtc"/> returns, and what NinjaTrader sends) is treated as Central time.
    /// </summary>
    public static long ToUnixMilliseconds(DateTime time)
    {
        var utc = time.Kind switch
        {
            DateTimeKind.Utc => time,
            DateTimeKind.Local => time.ToUniversalTime(),
            _ => TimeZoneInfo.ConvertTimeToUtc(time, Central)
        };
        return new DateTimeOffset(utc).ToUnixTimeMilliseconds();
    }

    // IANA id on Linux and on .NET 6+ Windows; the Windows id covers .NET Framework.
    private static TimeZoneInfo findCentral()
    {
        foreach (var id in new[] { "America/Chicago", "Central Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        throw new TimeZoneNotFoundException("The US Central time zone (America/Chicago) is not available on this system.");
    }
}
