using System.Globalization;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Import;

public class TickImporter
{
    private const string Instrument = "MES";

    public static async Task<int> ImportTickData(string file, ITickRepository tickRepository, int batchSize)
    {
        if (!File.Exists(file))
            return 0;

        var ticks = new List<TickData>();
        using var reader = new StreamReader(file);
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            var tick = parseTickData(line);
            if (tick != null) ticks.Add(tick);
        }

        if (ticks.Count > 0)
            return await tickRepository.InsertBatchAsync(ticks, batchSize);

        return 0;
    }

    private static TickData? parseTickData(string line)
    {
        var parts = line.Split(';');
        if (parts.Length < 5) return null;
        try
        {
            string dateTimeRaw = parts[0].Trim();
            string[] dtParts = dateTimeRaw.Split(' ');
            if (dtParts.Length > 3 || dtParts.Length < 2) return null;

            string datePart = dtParts[0];
            string timePart = dtParts[1];
            string fracPart = dtParts.Length == 3 ? dtParts[2] : "0";

            DateTime dt = DateTime.ParseExact(datePart + " " + timePart, "yyyyMMdd HHmmss", CultureInfo.InvariantCulture);
            if (long.TryParse(fracPart, out long microseconds))
                dt = dt.AddMilliseconds(microseconds / 1000.0);

            float lastPrice = float.Parse(parts[1], CultureInfo.InvariantCulture);
            float bid = float.Parse(parts[2], CultureInfo.InvariantCulture);
            float ask = float.Parse(parts[3], CultureInfo.InvariantCulture);
            var   vol = parts[4].Split(['.'], StringSplitOptions.RemoveEmptyEntries)[0];
            int   volume = int.Parse(vol);
            long  tickTime = new DateTimeOffset(dt).ToUnixTimeMilliseconds();

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
