using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutomatedTrading.Core.Data;
using AutomatedTrading.Core.Data.Commands;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Import;

public class BarImporter
{
    private const string FILE_SUFFIX = ".Last.txt";

    // fileName is the original name, formatted "[instrument] [mm-yy].Last.txt" (e.g. "MES 03-26.Last.txt");
    // `file` is the path actually read, which may be a temp copy with a different name.
    public static async Task<int> ImportBarData(
        string file,
        string fileName,
        GetOrCreateInstrument getOrCreateInstrument,
        GetOrCreateInstrumentVersion getOrCreateInstrumentVersion,
        IDefaultInsertCommand<BarData> barRepository)
    {
        if (!File.Exists(file))
            return 0;

        var (instrument, version) = parseFileName(fileName);
        var instrumentId = await getOrCreateInstrument.Execute(instrument);
        var versionId = await getOrCreateInstrumentVersion.Execute(instrumentId, version);

        var bars = new List<BarData>();
        using var reader = new StreamReader(file);
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var bar = parseBarData(line, instrument, (int)instrumentId, (int)versionId);
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

    private static (string Instrument, string Version) parseFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (!name.EndsWith(FILE_SUFFIX, StringComparison.OrdinalIgnoreCase))
            throw new FormatException($"'{fileName}' does not match '[instrument] [mm-yy]{FILE_SUFFIX}'.");

        var parts = name[..^FILE_SUFFIX.Length].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !DateTime.TryParseExact(parts[1], "MM-yy", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new FormatException($"'{fileName}' does not match '[instrument] [mm-yy]{FILE_SUFFIX}'.");

        return (parts[0], parts[1]);
    }

    private static BarData? parseBarData(string line, string instrument, int instrumentId, int versionId)
    {
        var parts = line.Split(';');
        var timestamp = DateTimeConverter.UtcStringToLocal(parts[0].Replace(" ", ""));
        var sessionDate = timestamp.Date;
        if (timestamp.Hour > 16)
            sessionDate = TradingCalendar.GetNextSessionDate(timestamp.Date);

        return new BarData
        {
            Instrument = instrument,
            InstrumentId = instrumentId,
            VersionId = versionId,
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
