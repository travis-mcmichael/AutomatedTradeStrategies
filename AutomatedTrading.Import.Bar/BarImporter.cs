using System.Globalization;
using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Import.Bar;

public class BarImporter
{
    public static async Task<int> ImportBarData(string file)
    {
        if (!File.Exists(file))
            return 0;

        var bars = new List<BarData>();
        var totalInserted = 0;
        using var reader = new StreamReader(file);
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var bar = parseBarData(line);
            if (bar == null) continue;

            bars.Add(bar);
        }

        try
        {
            if (bars.Count > 0)
            {
                totalInserted = await SqliteDataLayer.InsertBarDataBatch(bars);
            }   
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
        

        return totalInserted;
    }

    
    private static BarData? parseBarData(string line)
    {
        var parts = line.Split(';');
        var dateTimePart = parts[0];
        var datePart = dateTimePart.Substring(0, 8);
        var timePart = dateTimePart.Substring(9, 6);
        var timestamp = DateTime.ParseExact(
            datePart + timePart,
            "yyyyMMddHHmmss",
            CultureInfo.InvariantCulture);
        var sessionDate = timestamp.Date;
        if (timestamp.Hour >= 16)
        {
            sessionDate = sessionDate.AddDays(1);
        }
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
            SessionDate = $"{sessionDate:yyyyMMdd}"
        };
    }
}