using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;
using Microsoft.VisualBasic;

namespace AutomatedTrading.Export.Feature.Exporters;

public class NextDayHighBarFeatureExporter
{
    private const int daysMin = 20;
    private const int daysPerChunk = 30;
    private bool headerWritten;
    private List<BarData> dayBuffer;
    private List<BarData> minuteBuffer;
    private string path;
    private int results;
    public NextDayHighBarFeatureExporter(string outputPath)
    {
        results = 0;
        headerWritten = false;
        path = outputPath;
        dayBuffer = new List<BarData>();
        minuteBuffer = new List<BarData>();
    }

    public async Task GenerateFeatures(DateTime start, DateTime end)
    {
        Console.WriteLine("Generating Features...");
        var chunkStart = start;
        while (chunkStart < end)
        {
            var chunkEnd = chunkStart.AddDays(daysPerChunk);
            if (chunkEnd > end)
                chunkEnd = end;

            Console.WriteLine($"Loading batch from database for {chunkStart:yyyy-MM-dd} - {chunkEnd:yyyy-MM-dd}");            
            using var reader = await SqliteDataLayer.GetBarDataReader(chunkStart, chunkEnd);
            while (await reader.ReadAsync())
            {
                var bar = new BarData
                {
                    Instrument = reader.GetString(0),
                    Timeframe = reader.GetString(1),
                    Timestamp = DateTime.Parse(reader.GetString(2)),
                    SessionDate = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Open = reader.GetDouble(4),
                    High = reader.GetDouble(5),
                    Low = reader.GetDouble(6),
                    Close = reader.GetDouble(7),
                    Volume = reader.GetDouble(8)
                };
                minuteBuffer.Add(bar);
            }
            
            convertMinutesToDays();
            chunkStart = chunkEnd;
        }
        await writeFeatures(getFeatures());

        Console.WriteLine("Generating Features complete");
    }

    private List<Feature> getFeatures()
    {
        Console.WriteLine($"Building feature collection from {dayBuffer.Count} day bars");
        var features = new List<Feature>();
        for (var i = 0; i < dayBuffer.Count; i++)
        {
            var bar = dayBuffer[i];
            if (i < daysMin)
                continue;
            var prev = dayBuffer[i - 1];
            var feature = new Feature 
            {
                Instrument = bar.Instrument,
                Timeframe = bar.Timeframe,
                Timestamp = bar.Timestamp,
                SessionDate = bar.SessionDate,
                Open = bar.Open,
                High = bar.High,
                Low = bar.Low,
                Close = bar.Close,
                Volume = bar.Volume,
                PrevHigh = prev.High,
                PrevLow = prev.Low,
                PrevRange = prev.High - prev.Low,
                PrevBodySize = Math.Abs(prev.Close - prev.Open),
                PrevUpperWick = prev.High - Math.Max(prev.Open, prev.Close),
                PrevLowerWick = Math.Min(prev.Open, prev.Close) - prev.Low,
                PrevClosePosition = (prev.Close - prev.Low) / (prev.High - prev.Low),
                ATR5 = Computations.ATR(dayBuffer, i, 5),
                ATR10 = Computations.ATR(dayBuffer, i, 10),
                ATR20 = Computations.ATR(dayBuffer, i, 20),
                AvgRange5 = Computations.AverageRange(dayBuffer, i, 5),
                AvgRange10 = Computations.AverageRange(dayBuffer, i, 10),
                RangeRatio = (prev.High - prev.Low) / Computations.AverageRange(dayBuffer, i, 10),
                Return1D = Computations.Return(dayBuffer, i, 1),
                Return3D = Computations.Return(dayBuffer, i, 3),
                Return5D = Computations.Return(dayBuffer, i, 5),
                SMA5 = Computations.SMA(dayBuffer, i, 5, x => x.Close),
                SMA10 = Computations.SMA(dayBuffer, i, 10, x => x.Close),
                SMA20 = Computations.SMA(dayBuffer, i, 20, x => x.Close),
                CloseSMA5Ratio = bar.Close / Computations.SMA(dayBuffer, i, 5, b => b.Close),
                CloseSMA10Ratio = bar.Close / Computations.SMA(dayBuffer, i, 10, b => b.Close),
                CloseSMA20Ratio = bar.Close / Computations.SMA(dayBuffer, i, 20, b => b.Close),
                VolumeMA20 = Computations.SMA(dayBuffer, i, 20, b => b.Volume),
                VolumeZScore20 = Computations.VolumeZScore(dayBuffer, i, 20),
                DayOfWeek = (int)bar.Timestamp.DayOfWeek,
                DayOfMonth = bar.Timestamp.Day,
                TargetNextSessionHigh = Computations.NextSessionHigh(dayBuffer, i)
            };
            features.Add(feature);
        }
        dayBuffer.Clear();
        Console.WriteLine($"Finished converting into {features.Count} features");
        return features;
    }

    private async Task writeFeatures(List<Feature> features)
    {
        Console.WriteLine($"Writing {features.Count} features to csv file...");
        using var writer = new StreamWriter(path);
        foreach (var feature in features)
        {
            if (!headerWritten)
            {
                await writer.WriteLineAsync(
                    "PrevHigh,PrevLow,PrevRange,PrevBodySize,PrevUpperWick,PrevLowerWick,PrevClosePosition," +
                    "ATR5,ATR10,ATR20," +
                    "AvgRange5,AvgRange10,RangeRatio," +
                    "Return1D,Return3D,Return5D," +
                    "SMA5,SMA10,SMA20," +
                    "CloseSMA5Ratio,CloseSMA10Ratio,CloseSMA20Ratio," +
                    "VolumeMA20,VolumeZScore20," +
                    "DayOfWeek,DayOfMonth," +
                    "TargetNextSessionHigh"
                );
                headerWritten = true;
            }

            await writer.WriteLineAsync(string.Join(",",
                feature.PrevHigh,
                feature.PrevLow,
                feature.PrevRange,
                feature.PrevBodySize,
                feature.PrevUpperWick,
                feature.PrevLowerWick,
                feature.PrevClosePosition,
                feature.ATR5,
                feature.ATR10,
                feature.ATR20,
                feature.AvgRange5,
                feature.AvgRange10,
                feature.RangeRatio,
                feature.Return1D,
                feature.Return3D,
                feature.Return5D,
                feature.SMA5,
                feature.SMA10,
                feature.SMA20,
                feature.CloseSMA5Ratio,
                feature.CloseSMA10Ratio,
                feature.CloseSMA20Ratio,
                feature.VolumeMA20,
                feature.VolumeZScore20,
                feature.DayOfWeek,
                feature.DayOfMonth,
                feature.TargetNextSessionHigh));
        }
        results += features.Count;
        Console.WriteLine($"Features written: {results} total so far.");
    }

    private void convertMinutesToDays()
    {
        Console.WriteLine("Converting minute bars to day bars...");
        var sessionDates = minuteBuffer.Select(x => x.SessionDate).Distinct();
        foreach (var date in sessionDates)
        {
            var bars = minuteBuffer.Where(x => x.SessionDate == date);
            if (bars.Count() > 0)
            {
                dayBuffer.Add(new BarData
                {
                    Open = bars.First().Open,
                    Close = bars.Last().Close,
                    High = bars.Max(x => x.High),
                    Low = bars.Min(x => x.Low),
                    Volume = bars.Sum(x => x.Volume),
                    SessionDate = bars.Last().SessionDate,
                    Timestamp = bars.Last().Timestamp,
                    Timeframe = "1d",
                    Instrument = bars.First().Instrument
                });
            }
        }
        Console.WriteLine($"Converted {minuteBuffer.Count} minutes into {dayBuffer.Count} day bars.");
        minuteBuffer.Clear();
    }

	class Feature : BarData
	{
		public double PrevHigh { get; set; }
		public double PrevLow { get; set; }
		public double PrevRange { get; set; }
		public double PrevBodySize { get; set; }
		public double PrevUpperWick { get; set; }
		public double PrevLowerWick { get; set; }
		public double PrevClosePosition { get; set; }
		public double ATR5 { get; set; }
		public double ATR10 { get; set; }
		public double ATR20 { get; set; }
		public double AvgRange5 { get; set; }
		public double AvgRange10 { get; set; }
		public double RangeRatio { get; set; }
		public double Return1D { get; set; }
		public double Return3D { get; set; }
		public double Return5D { get; set; }
		public double SMA5 { get; set; }
		public double SMA10 { get; set; }
		public double SMA20 { get; set; }
		public double CloseSMA5Ratio { get; set; }
		public double CloseSMA10Ratio { get; set; }
		public double CloseSMA20Ratio { get; set; }
		public double VolumeMA20 { get; set; }
		public double VolumeZScore20 { get; set; }
		public int DayOfWeek { get; set; }
		public int DayOfMonth { get; set; }
		public double TargetNextSessionHigh { get; set; }
	}
}