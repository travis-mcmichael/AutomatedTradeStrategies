using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Export.Feature.Exporters;

public class NextDayHighBarFeatureExporter
{
    private const int daysMin = 20;
    private const int daysPerChunk = 30;
    private const string span = "2h";
    private bool headerWritten;
    private List<BarDataExtended> sequenceBuffer;
    private List<BarDataExtended> minuteBuffer;
    private string path;
    private int results;
    public NextDayHighBarFeatureExporter(string outputPath)
    {
        results = 0;
        headerWritten = false;
        path = outputPath;
        sequenceBuffer = new List<BarDataExtended>();
        minuteBuffer = new List<BarDataExtended>();
    }

    public async Task GenerateFeatures(DateTime start, DateTime end)
    {
        Console.WriteLine("Generating Features...");
        var chunkStart = start;
        var currentSessionDate = "";
        double? sessionOpen = null, prevClose = null;
        while (chunkStart < end)
        {
            var chunkEnd = chunkStart.AddDays(daysPerChunk);
            if (chunkEnd > end)
                chunkEnd = end;

            Console.WriteLine($"Loading batch from database for {chunkStart:yyyy-MM-dd} - {chunkEnd:yyyy-MM-dd}");            
            using var reader = await SqliteDataLayer.GetBarDataReader(chunkStart, chunkEnd);
            while (await reader.ReadAsync())
            {
                var bar = new BarDataExtended
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

                if (currentSessionDate != bar.SessionDate)
                {
                    sessionOpen = bar.Open;
                    if (minuteBuffer.Any())
                    {
                        prevClose = minuteBuffer.Last().Close;
                    }
                    else
                    {
                        prevClose = sessionOpen;
                    }
                    currentSessionDate = bar.SessionDate;
                }
                bar.SessionOpen = sessionOpen;
                bar.PreviousClose = prevClose;

                minuteBuffer.Add(bar);
            }

            adaptMinutesToFeatures();
            chunkStart = chunkEnd;
        }
        await writeFeatures(getFeatures());

        Console.WriteLine("Generating Features complete");
    }

    private List<Feature> getFeatures()
    {
        Console.WriteLine($"Building feature collection from {sequenceBuffer.Count} {span} bars");
        var features = new List<Feature>();

        sequenceBuffer = sequenceBuffer.OrderBy(x => x.Timestamp).ToList();
        var buffer = new List<BarData>();
        foreach (var sequence in sequenceBuffer)
            buffer.Add(sequence);

        for (var i = 0; i < buffer.Count; i++)
        {
            var bar = sequenceBuffer[i];
            if (i < daysMin || bar.High == bar.Low)
                continue;
            
            try
            {
                var atr5 = Computations.ATR(buffer, i, 5);
                var atr10 = Computations.ATR(buffer, i, 10);
                var atr20 = Computations.ATR(buffer, i, 20);
                var sma5 = Computations.SMA(buffer, i, 5, x => x.Close);
                var sma10 = Computations.SMA(buffer, i, 10, x => x.Close);
                var sma20 = Computations.SMA(buffer, i, 20, x => x.Close);
                var avgRange10 = Computations.AverageRange(buffer, i, 10);
                var vwap = Computations.SessionVWAP(buffer, i);
                var rangeSoFar = Computations.SessionRange(buffer, i);

                var prev = buffer[i - 1];
                var feature = new Feature
                {
                    Timestamp = bar.Timestamp,
                    PrevHigh = Operations.Safe(prev.High),
                    PrevLow = prev.Low,
                    PrevRange = Computations.HighLowRange(prev, atr20),
                    PrevBodySize = Computations.BodySize(prev, atr20),
                    PrevUpperWick = Computations.UpperWick(prev, atr20),
                    PrevLowerWick = Computations.LowerWick(prev, atr20),
                    PrevClosePosition = Computations.ClosePosition(prev),
                    ATR5 = atr5 / bar.Close,
                    ATR10 = atr10 / bar.Close,
                    ATR20 = atr20 / bar.Close,
                    SMA5 = sma5,
                    SMA10 = sma10,
                    SMA20 = sma20,
                    AvgRange5 = Computations.AverageRange(buffer, i, 5),
                    AvgRange10 = avgRange10,
                    RangeRatio = avgRange10 == 0 ? double.NaN : ((prev.High - prev.Low) / avgRange10),
                    Return1D = Computations.Return(buffer, i, 1),
                    Return3D = Computations.Return(buffer, i, 3),
                    Return5D = Computations.Return(buffer, i, 5),
                    CloseSMA5Ratio = bar.Close / (sma5 == 0 ? double.NaN : sma5),
                    CloseSMA10Ratio = bar.Close / (sma10 == 0 ? double.NaN : sma10),
                    CloseSMA20Ratio = bar.Close / (sma20 == 0 ? double.NaN : sma20),
                    VolumeMA20 = Computations.SMA(buffer, i, 20, b => b.Volume),
                    VolumeZScore20 = Computations.VolumeZScore(buffer, i, 20),
                    DayOfWeek = (int)bar.Timestamp.DayOfWeek,
                    DayOfMonth = bar.Timestamp.Day,
                    DistanceFromPrevHigh = atr20 == 0 ? 0 : (bar.Close - prev.High) / atr20,
                    DistanceFromPrevLow = atr20 == 0 ? 0 : (bar.Close - prev.Low) / atr20,
                    RangeExpansionRatio = avgRange10 > 0 ? rangeSoFar / avgRange10 : double.NaN,
                    VolatilityRegime = atr20 == 0 ? 0 : atr10 / atr20,
                    VWAPDistance = atr20 == 0 ? 0 : (bar.Close - vwap) / atr20,
                    GapVsAtr = atr20 == 0 ? 0 : ((bar.SessionOpen ?? 0) - prev.Close) / atr20,
                    TargetNextSessionHigh = Computations.NextSessionHigh(buffer, i, atr20)
                };

                features.Add(feature);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
        sequenceBuffer.Clear();
        Console.WriteLine($"Finished converting into {features.Count} features");
        return features;
    }

    private async Task writeFeatures(List<Feature> features)
    {
        Console.WriteLine($"Writing {features.Count} features to csv file...");
        using var writer = new StreamWriter(path);
        features = features.OrderBy(x => x.Timestamp).ToList();
        foreach (var feature in features)
        {
            if (!headerWritten)
            {
                await writer.WriteLineAsync(
                    "PrevHigh,PrevLow,PrevRange,PrevBodySize,PrevUpperWick,PrevLowerWick,PrevClosePosition," +
                    "ATR5,ATR10,ATR20," +
                    "AvgRange5,AvgRange10,RangeRatio," +
                    "Return1D,Return3D,Return5D," +
                    "SMA5, SMA10, SMA20," +
                    "CloseSMA5Ratio,CloseSMA10Ratio,CloseSMA20Ratio," +
                    "VolumeMA20,VolumeZScore20," +
                    "DistanceFromPrevHigh,DistanceFromPrevLow,RangeExpansionRatio,VolatilityRegime,VWAPDistance," +
                    "GapVsAtr,DayOfWeek,DayOfMonth,TargetNextSessionHigh"
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
                feature.DistanceFromPrevHigh,
                feature.DistanceFromPrevLow,
                feature.RangeExpansionRatio,
                feature.VolatilityRegime,
                feature.VWAPDistance,
                feature.GapVsAtr,
                feature.DayOfWeek,
                feature.DayOfMonth,
                feature.TargetNextSessionHigh));
                Console.WriteLine(feature.ToLog());
        }
        results += features.Count;
        Console.WriteLine($"Features written: {results} total so far.");
    }

    private void checkSequence(IEnumerable<BarDataExtended> bars, string timeframe)
    {
        if (bars.Any())
        {
            bars = bars.OrderBy(x => x.Timestamp);
            var bar = new BarDataExtended
            {
                Open = bars.First().Open,
                Close = bars.Last().Close,
                High = bars.Max(x => x.High),
                Low = bars.Min(x => x.Low),
                Volume = bars.Sum(x => x.Volume),
                SessionDate = bars.Last().SessionDate,
                Timestamp = bars.Last().Timestamp,
                Timeframe = timeframe,
                Instrument = bars.First().Instrument,
                SessionOpen = bars.First().SessionOpen,
                PreviousClose = bars.First().PreviousClose,
            };
            Console.WriteLine($"{bar.ToLog()}");
            sequenceBuffer.Add(bar);

            sequenceBuffer = sequenceBuffer.OrderBy(x => x.Timestamp).ToList();
        }
    }

    private void adaptMinutesToFeatures()
    {
        Console.WriteLine("Converting minute bars to day bars...");
        var sessionDates = minuteBuffer.Select(x => x.SessionDate).Distinct().OrderBy(x => x).ToList();
        var current = sequenceBuffer.Count;
        foreach (var date in sessionDates)
        {
            var bars = minuteBuffer.Where(x => x.SessionDate == date).OrderBy(x => x.Timestamp).ToList();
            if (bars.Any())
            {
                checkSequence(bars.Where(x => x.Timestamp.Hour >= 16 && x.Timestamp.Hour < 18), span);
                checkSequence(bars.Where(x => x.Timestamp.Hour >= 18 && x.Timestamp.Hour < 20), span);
                checkSequence(bars.Where(x => x.Timestamp.Hour >= 20 && x.Timestamp.Hour < 22), span);
                checkSequence(bars.Where(x => x.Timestamp.Hour >= 22), span);
                checkSequence(bars.Where(x => x.Timestamp.Hour >= 0 && x.Timestamp.Hour < 2), span);
                checkSequence(bars.Where(x => x.Timestamp.Hour >= 2 && x.Timestamp.Hour < 4), span);
                checkSequence(bars.Where(x => x.Timestamp.Hour >= 4 && x.Timestamp.Hour < 6), span);
                checkSequence(bars.Where(x => x.Timestamp.Hour >= 6 && x.Timestamp.Hour < 8), span);
                checkSequence(bars.Where(x => x.Timestamp.Hour >= 8 && x.Timestamp.Hour < 10), span);
                checkSequence(bars.Where(x => x.Timestamp.Hour >= 10 && x.Timestamp.Hour < 12), span);
                checkSequence(bars.Where(x => x.Timestamp.Hour >= 12 && x.Timestamp.Hour < 14), span);
                checkSequence(bars.Where(x => x.Timestamp.Hour >= 14 && x.Timestamp.Hour < 16), span);
            }
        }
        Console.WriteLine($"Converted {minuteBuffer.Count} minutes into {sequenceBuffer.Count - current} {span} bars.");
        minuteBuffer.Clear();
    }

    class BarDataExtended : BarData
    {
        public double? SessionOpen { get; set; }
        public double? PreviousClose { get; set; }
        public override string ToLog()
        {
            return $"{base.ToLog()},sessionopen={SessionOpen},previousclose={PreviousClose}";
        }
    }

	class Feature
	{
        public DateTime Timestamp { get; set; }
		public double? PrevHigh { get; set; }
		public double? PrevLow { get; set; }
		public double? PrevRange { get; set; }
		public double? PrevBodySize { get; set; }
		public double? PrevUpperWick { get; set; }
		public double? PrevLowerWick { get; set; }
		public double? PrevClosePosition { get; set; }
		public double? ATR5 { get; set; }
		public double? ATR10 { get; set; }
		public double? ATR20 { get; set; }
		public double? AvgRange5 { get; set; }
		public double? AvgRange10 { get; set; }
		public double? RangeRatio { get; set; }
		public double? Return1D { get; set; }
		public double? Return3D { get; set; }
		public double? Return5D { get; set; }
		public double? SMA5 { get; set; }
		public double? SMA10 { get; set; }
		public double? SMA20 { get; set; }
		public double? CloseSMA5Ratio { get; set; }
		public double? CloseSMA10Ratio { get; set; }
		public double? CloseSMA20Ratio { get; set; }
		public double? VolumeMA20 { get; set; } 
		public double? VolumeZScore20 { get; set; }
		public int? DayOfWeek { get; set; }
		public int? DayOfMonth { get; set; }
        public double? DistanceFromPrevHigh { get; set; }
        public double? DistanceFromPrevLow { get; set; }
        public double? RangeExpansionRatio { get; set; }
        public double? VolatilityRegime { get; set; }
        public double? VWAPDistance { get; set; }
        public double? GapVsAtr { get; set; }
		public double? TargetNextSessionHigh { get; set; }
        public string ToLog()
        {
            return $"prevhigh={PrevHigh},prevlow={PrevLow},prevrange={PrevRange},prevbodysize={PrevBodySize},";
            // + $"prevupperwick={PrevUpperWick},prevlowerwick={PrevLowerWick},prevcloseposition={PrevClosePosition},"
            // + $"atr5={ATR5},atr10={ATR10},atr20={ATR20},avgrange5={AvgRange5},avgrange10={AvgRange10}," 
            // + $"rangeratio={RangeRatio},return1d={Return1D},return3d={Return3D},return5d={Return5D}," 
            // + $"sma5={SMA5},sma10={SMA10},sma20={SMA20},closesma5ratio={CloseSMA5Ratio}," 
            // + $"closesma10ratio={CloseSMA10Ratio},closesma20ratio={CloseSMA20Ratio}," 
            // + $"volumema20={VolumeMA20},volumezscore20={VolumeZScore20},dayofweek={DayOfWeek}," 
            // + $"dayofmonth={DayOfMonth},distancefromprevhigh={DistanceFromPrevHigh}," 
            // + $"distancefromprevlow={DistanceFromPrevLow},rangeexpansionratio={RangeExpansionRatio}," 
            // + $"volatilityregime={VolatilityRegime},vwapdistance={VWAPDistance},gapvsatr={GapVsAtr}," 
            // + $"targetnextsessionhigh={TargetNextSessionHigh}";
    	}
    }
}