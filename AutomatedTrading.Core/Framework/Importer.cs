using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using AutomatedTrading.Core.Models;
using Microsoft.Data.Sqlite;

namespace AutomatedTrading.Core.Framework;

public class Importer
{
    private static int tickImportBatchSize = 250;

    public static async Task<int> ImportTickData(string file)
    {
        if (!File.Exists(file))
        return 0;

        var tickBatch = new List<TickData>();
        var totalInserted = 0;

        await connection.OpenAsync();
        using var reader = new StreamReader(file);
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            var tick = parseTickData(line);
            if (tick == null) continue;
            
            tickBatch.Add(tick);
            if (tickBatch.Count >= tickImportBatchSize)
            {
                var inserted = await InsertBatchAsync.Insert(connection, tickBatch);
                totalInserted += inserted;
                tickBatch.Clear();
            }
        }
        
        if (tickBatch.Count > 0)
        {
            var inserted = await InsertBatchAsync.Insert(connection, tickBatch);
            totalInserted += inserted;
        }

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