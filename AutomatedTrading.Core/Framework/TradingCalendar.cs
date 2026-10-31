using System;
using System.Collections.Generic;
using System.Globalization;

namespace AutomatedTrading.Core.Framework;

public static class TradingCalendar
{
    // Holiday sets are computed once per year and cached.
    private static readonly Dictionary<int, HashSet<DateTime>> _cache = new();

    /// <summary>
    /// Returns the next trading day after <paramref name="date"/>, skipping
    /// weekends and US market holidays.
    /// </summary>
    public static DateTime GetNextSessionDate(DateTime date)
    {
        var next = date.Date.AddDays(1);
        while (!IsTradingDay(next))
            next = next.AddDays(1);
        return next;
    }

    /// <summary>
    /// Returns the previous trading day before <paramref name="date"/>, skipping
    /// weekends and US market holidays.
    /// </summary>
    public static DateTime GetPreviousSessionDate(DateTime date)
    {
        var prev = date.Date.AddDays(-1);
        while (!IsTradingDay(prev))
            prev = prev.AddDays(-1);
        return prev;
    }

    public static bool IsTradingDay(string date)
    {
        if (date.Length == 8)
        {
            return IsTradingDay(DateTime.ParseExact(date, AppConstants.DATE_FORMAT, CultureInfo.InvariantCulture));
        }
        else if (date.Length == 6)
        {
            return IsTradingDay(DateTime.ParseExact(date, "yyMMdd", CultureInfo.InvariantCulture));
        }
        else if (date.Length == 4)
        {
            return IsTradingDay(DateTime.ParseExact(date, "MMdd", CultureInfo.InvariantCulture));
        }
        throw new InvalidCastException($"Bad date string: {date}");
    }

    /// <summary>
    /// Returns true when <paramref name="date"/> is a weekday that is not a
    /// US market holiday.
    /// </summary>
    public static bool IsTradingDay(DateTime date)
    {
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return false;

        return !GetHolidaysForYear(date.Year).Contains(date.Date);
    }

    public static bool IsHolidayAdjacent(DateTime date)
    {
        var next = date.AddDays(1);
        while (next.DayOfWeek == DayOfWeek.Saturday || next.DayOfWeek == DayOfWeek.Sunday)
        {
            next = next.AddDays(1);
        }
        var holidays = GetHolidaysForYear(date.Year);
        if (holidays.Contains(next))
            return true;
        var prev = date.AddDays(-1);
        while (prev.DayOfWeek == DayOfWeek.Saturday || prev.DayOfWeek == DayOfWeek.Sunday)
        {
            prev = prev.AddDays(-1);
        }
        return holidays.Contains(prev);
    }

    // ─── Holiday computation ──────────────────────────────────────────────────

    private static HashSet<DateTime> GetHolidaysForYear(int year)
    {
        if (_cache.TryGetValue(year, out var cached))
            return cached;

        var holidays = new HashSet<DateTime>
        {
            // New Year's Day — Jan 1
            Observed(new DateTime(year, 1, 1)),

            // Martin Luther King Jr. Day — 3rd Monday of January
            NthWeekday(year, 1, DayOfWeek.Monday, 3),

            // Presidents' Day — 3rd Monday of February
            NthWeekday(year, 2, DayOfWeek.Monday, 3),

            // Good Friday — Friday before Easter Sunday
            EasterSunday(year).AddDays(-2),

            // Memorial Day — last Monday of May
            LastWeekday(year, 5, DayOfWeek.Monday),

            // Juneteenth — Jun 19
            Observed(new DateTime(year, 6, 19)),

            // Independence Day — Jul 4
            Observed(new DateTime(year, 7, 4)),

            // Labor Day — 1st Monday of September
            NthWeekday(year, 9, DayOfWeek.Monday, 1),

            // Thanksgiving — 4th Thursday of November
            NthWeekday(year, 11, DayOfWeek.Thursday, 4),

            // Christmas — Dec 25
            Observed(new DateTime(year, 12, 25)),
        };

        _cache[year] = holidays;
        return holidays;
    }

    /// <summary>
    /// Shifts a fixed holiday to the nearest weekday when it falls on a weekend:
    /// Saturday → Friday, Sunday → Monday.
    /// </summary>
    private static DateTime Observed(DateTime holiday) =>
        holiday.DayOfWeek switch
        {
            DayOfWeek.Saturday => holiday.AddDays(-1),
            DayOfWeek.Sunday => holiday.AddDays(1),
            _ => holiday,
        };

    /// <summary>Returns the nth occurrence of <paramref name="dow"/> in the given month.</summary>
    private static DateTime NthWeekday(int year, int month, DayOfWeek dow, int n)
    {
        var first = new DateTime(year, month, 1);
        var daysToFirst = ((int)dow - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(daysToFirst + (n - 1) * 7);
    }

    /// <summary>Returns the last occurrence of <paramref name="dow"/> in the given month.</summary>
    private static DateTime LastWeekday(int year, int month, DayOfWeek dow)
    {
        var last = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        var daysBack = ((int)last.DayOfWeek - (int)dow + 7) % 7;
        return last.AddDays(-daysBack);
    }

    /// <summary>
    /// Computes Easter Sunday using the Anonymous Gregorian algorithm.
    /// </summary>
    private static DateTime EasterSunday(int year)
    {
        int a = year % 19;
        int b = year / 100;
        int c = year % 100;
        int d = b / 4;
        int e = b % 4;
        int f = (b + 8) / 25;
        int g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4;
        int k = c % 4;
        int l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int month = (h + l - 7 * m + 114) / 31;
        int day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateTime(year, month, day);
    }
}