using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using AutomatedTrading.Core.Models;
namespace AutomatedTrading.Core.Tests;

public class LoadTestData
{
    private readonly string? basePath;

    public LoadTestData(string? basePath = null)
    {
        this.basePath = basePath;
    }

    public List<Bar> LoadAll()
    {
        var bars = new List<Bar>();
        if (!Directory.Exists(basePath))
            throw new DirectoryNotFoundException($"Directory not found: {basePath}.");

        var files = Directory.GetFiles(basePath, "*.dat");
        foreach (var file in files)
        {
            bars.AddRange(LoadFile(file));
        }

        return bars;
    }

    public List<Bar> LoadFile(string file, string date = null)
    {
        var filePath = Path.Combine(basePath, file);
        var bars = new List<Bar>();

        foreach (var line in File.ReadAllLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split(';');
            if (parts.Length != 6)
                continue;

            // This is specifically to correct the UTC offset from exported NinjaScript files from an instrument on Central Time.  
            // If this is being modified to import from another data source, the offset would need to be reassessed.
            var dateTime = DateTime.ParseExact(parts[0], "yyyyMMdd HHmmss", CultureInfo.InvariantCulture)
                .AddHours(AppConstants.TEST_DATA_OFFSET_HOURS);

            // if date has a value and the line date doesn't match, skip
            if (!string.IsNullOrEmpty(date) && parts[0].Split(' ').First() != date)
            {
                continue;
            }

            var open = double.Parse(parts[1], CultureInfo.InvariantCulture);
            var high = double.Parse(parts[2], CultureInfo.InvariantCulture);
            var low = double.Parse(parts[3], CultureInfo.InvariantCulture);
            var close = double.Parse(parts[4], CultureInfo.InvariantCulture);
            var volume = double.Parse(parts[5], CultureInfo.InvariantCulture);

            var numericTime = int.Parse(dateTime.ToString("HHmmss"));
            bars.Add(new Bar(numericTime, open, high, low, close, volume));
        }

        return bars;
    }
}