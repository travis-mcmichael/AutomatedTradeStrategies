using System.Globalization;

namespace AutomatedTrading.Import;

public static class DateTimeConverter
{
    private const string UtcFormat = "yyyyMMddHHmmss";

    /// <summary>
    /// Parses a UTC datetime string ("yyyyMMddHHmmss") and converts it to the
    /// local system time, correctly accounting for Daylight Saving Time transitions.
    /// </summary>
    public static DateTime UtcStringToLocal(string utcDateTime)
    {
        var utc = DateTime.ParseExact(
            utcDateTime,
            UtcFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        return TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.Local);
    }
}
