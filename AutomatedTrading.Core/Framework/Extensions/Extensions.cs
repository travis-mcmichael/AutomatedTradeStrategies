using System;
using Microsoft.Data.Sqlite;

namespace AutomatedTrading.Core.Framework.Extensions;

public static class Extensions
{
    public static int ToInt(this DateTime date)
    {
        return date.Hour * 10000 + date.Minute * 1000;
    }
}