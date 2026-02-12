using System.Linq;
using System.Text;
using System.Collections.Generic;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Framework;

public static class LogBuilder
{
    private static string TimeIntToString(int time)
    {
        var s = time.ToString();
        while (s.Length < 6)
            s = "0" + s;
        return $"{s.Substring(0, 2)}:{s.Substring(2, 2)}:{s.Substring(4)}";
    }

    public static string GetEvaluationResultLog(Bar bar, string logHeader, Dictionary<string, object> logValues)
    {
        return $"[{TimeIntToString(bar.Time)}] {logHeader}\r\n\t{bar.ToLog()}\r\n\t{string.Join("\r\n\t", logValues.Select(x => x.Value.ToString()))}"; ;
    }

    public static string GetSessionSummaryLog(IMarketState marketState)
    {
        var outliers = marketState.Peaks.Select(x => new { Type = "Peak", Bar = x })
            .Concat(marketState.Valleys.Select(x => new { Type = "Valley", Bar = x }))
            .OrderBy(x => x.Bar.Time);
        var sb = new StringBuilder();

        foreach (var outlier in outliers)
        {
            if (outlier.Type == "Peak")
            {
                sb.AppendLine($"[{outlier.Bar.Time:yyyy-MM-dd HH:mm:ss}] Peak @ {outlier.Bar.High}");
            }
            else
            {
                sb.AppendLine($"[{outlier.Bar.Time:yyyy-MM-dd HH:mm:ss}] Valley @ {outlier.Bar.Low}");
            }
        }
        return sb.ToString();
    }
}