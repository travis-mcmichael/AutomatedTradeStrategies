using System.Globalization;
using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Import;

public class TickImporter
{
    private const string Instrument = "MES";

    public static async Task<int> ImportTickData(string file)
    {
        if (!File.Exists(file))
            return 0;

        var tickBatch = new List<TickData>();
        var totalInserted = 0;

        using var reader = new StreamReader(file);
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            var tick = parseTickData(line);
            if (tick == null) continue;

            tickBatch.Add(tick);
        }

        if (tickBatch.Count > 0)
            totalInserted = await SqliteDataLayer.InsertTickBatch(tickBatch);

        return totalInserted;
    }

    private static TickData? parseTickData(string line)
    {
        var parts = line.Split(';');
        if (parts.Length < 5) return null;
        try
        {
            // First field: "20251222 060000 3400000"
            string dateTimeRaw = parts[0].Trim();
            string[] dtParts = dateTimeRaw.Split(' ');
            if (dtParts.Length > 3 || dtParts.Length < 2) return null;

            string datePart = dtParts[0]; // 20251222
            string timePart = dtParts[1]; // 060000
            string fracPart = dtParts.Length == 3 ? dtParts[2] : "0"; // microseconds

            DateTime dt = DateTime.ParseExact(datePart + " " + timePart,
                                              "yyyyMMdd HHmmss",
                                              CultureInfo.InvariantCulture);

            if (long.TryParse(fracPart, out long microseconds))
                dt = dt.AddMilliseconds(microseconds / 1000.0);

            float lastPrice = float.Parse(parts[1], CultureInfo.InvariantCulture);
            float bid = float.Parse(parts[2], CultureInfo.InvariantCulture);
            float ask = float.Parse(parts[3], CultureInfo.InvariantCulture);
            var vol = parts[4].Split(['.'], StringSplitOptions.RemoveEmptyEntries)[0];
            int volume = int.Parse(vol);
            long tickTime = new DateTimeOffset(dt).ToUnixTimeMilliseconds();

            return new TickData
            {
                Instrument = Instrument,
                TickTime = tickTime,
                LastPrice = lastPrice,
                Volume = volume,
                Bid = bid,
                Ask = ask
            };
        }
        catch
        {
            return null;
        }
    }
}
