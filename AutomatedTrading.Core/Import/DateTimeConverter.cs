using System;
using System.Globalization;
using AutomatedTrading.Core.Framework;

namespace AutomatedTrading.Core.Import;

public static class DateTimeConverter
{
    private const string UtcFormat = "yyyyMMddHHmmss";

    /// <summary>
    /// Parses a UTC datetime string ("yyyyMMddHHmmss") and converts it to US Central (exchange) time,
    /// correctly accounting for Daylight Saving Time transitions. See <see cref="MarketTime"/>.
    /// </summary>
    public static DateTime UtcStringToCentral(string utcDateTime)
    {
        var utc = DateTime.ParseExact(
            utcDateTime,
            UtcFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        return MarketTime.FromUtc(utc);
    }
}
