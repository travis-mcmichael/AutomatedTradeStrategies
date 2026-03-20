using System;

namespace AutomatedTrading.Core.Framework;

public static class Extensions
{
    public static int ToInt(this DateTime date)
    {
        return date.Hour * 10000 + date.Minute * 100 + date.Second;
    }
}