using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutomatedTrading.Core.Data;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Import;

public class BarImporter
{
    public static async Task<int> ImportBarData(string file, string instrument, IDefaultInsertCommand<BarData> barRepository)
    {
        if (!File.Exists(file))
            return 0;

        var bars = new List<BarData>();
        using var reader = new StreamReader(file);
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var bar = parseBarData(line, instrument);
            if (bar != null) bars.Add(bar);
        }

        try
        {
            if (bars.Count > 0)
            {
                var inserted = await barRepository.Execute(bars);
                return inserted.Count();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }

        return 0;
    }

    private static BarData? parseBarData(string line, string instrument)
    {
        var parts = line.Split(';');
        var timestamp = DateTimeConverter.UtcStringToLocal(parts[0].Replace(" ", ""));
        var sessionDate = timestamp.Date;
        if (timestamp.Hour > 16)
            sessionDate = TradingCalendar.GetNextSessionDate(timestamp.Date);

        return new BarData
        {
            Instrument = instrument,
            Timeframe = "1m",
            Timestamp = timestamp,
            Open = double.Parse(parts[1], CultureInfo.InvariantCulture),
            High = double.Parse(parts[2], CultureInfo.InvariantCulture),
            Low = double.Parse(parts[3], CultureInfo.InvariantCulture),
            Close = double.Parse(parts[4], CultureInfo.InvariantCulture),
            Volume = double.Parse(parts[5], CultureInfo.InvariantCulture),
            SessionDate = $"{sessionDate:yyyyMMdd}",
            SessionTime = $"{timestamp:HHmmss}"
        };
    }
}
