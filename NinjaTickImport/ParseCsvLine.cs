using System.Globalization;
using AutomatedTrading.Core.Models;
namespace NinjaTickDataImport;
public static class ParseCsvLine
{
    public static TickData? Parse(string line)
    {
        var parts = line.Split(';');
        if (parts.Length < 5) return null;
        try
        {
            // First field: "20251222 060000 3400000"
        string dateTimeRaw = parts[0].Trim();
        // Split into date + time + fractional
        string[] dtParts = dateTimeRaw.Split(' ');
        if (dtParts.Length > 3 || dtParts.Length < 2) return null;

        string datePart = dtParts[0]; // 20251222
        string timePart = dtParts[1]; // 060000
        string fracPart = dtParts.Length == 3 ? dtParts[2] : "0"; // 3400000 microseconds

        // Parse date and time
        DateTime dt = DateTime.ParseExact(datePart + " " + timePart,
                                          "yyyyMMdd HHmmss",
                                          CultureInfo.InvariantCulture);

        // Add fractional seconds
        if (long.TryParse(fracPart, out long microseconds))
        {
            dt = dt.AddMilliseconds(microseconds / 1000.0);
        }

        // Other fields
        float lastPrice = float.Parse(parts[1], CultureInfo.InvariantCulture);
        float bid = float.Parse(parts[2], CultureInfo.InvariantCulture);
        float ask = float.Parse(parts[3], CultureInfo.InvariantCulture);
        var vol = parts[4].Split('.', StringSplitOptions.RemoveEmptyEntries)[0];
        int volume = int.Parse(vol);
        long tickTime = new DateTimeOffset(dt).ToUnixTimeMilliseconds();

        return new TickData
        {
            Instrument = "ESMAR26", // hardcoded or set dynamically
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