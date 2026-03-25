using System.Globalization;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Import;

public class BarImporter
{
    public static async Task<int> ImportBarData(string file, IBarRepository barRepository, int batchSize)
    {
        if (!File.Exists(file))
            return 0;

        var bars = new List<BarData>();
        using var reader = new StreamReader(file);
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var bar = parseBarData(line);
            if (bar != null) bars.Add(bar);
        }

        try
        {
            if (bars.Count > 0)
                return await barRepository.InsertBatchAsync(bars, batchSize);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }

        return 0;
    }

    private static BarData? parseBarData(string line)
    {
        var parts        = line.Split(';');
        var timestamp    = DateTimeConverter.UtcStringToLocal(parts[0]);
        var sessionDate  = timestamp.Date;
        if (timestamp.Hour >= 16)
            sessionDate = TradingCalendar.GetNextSessionDate(timestamp.Date);

        return new BarData
        {
            Instrument = "MES",
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
