using System.Globalization;
using AutomatedTrading.Core.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

public class ScalpStrategyEvaluator
{
    readonly string ModelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NeuralNetwork", "atr_scalp_model.onnx");
    readonly string MetaModelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NeuralNetwork", "atr_scalp_meta_model.onnx");
    readonly string ScalerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NeuralNetwork", "atr_scalp_scaler.csv");

    // =====================================================================
    // Constants
    // =====================================================================
    private const int FeatureCount = 8;
    private const int MetaFeatureCount = 9;    // 8 scaled + base probability
    private const int ZScoreLookback = 10;
    private const int ReturnLookback = 120;
    private string? baseInputName;
    private string? metaInputName;
    private InferenceSession? baseSession;
    private InferenceSession? metaSession;
    private double[]? scalerMean;
    private double[]? scalerScale;
    public bool ScalerLoaded = false;

    public void LoadModels()
    {
        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Evaluator.LoadModels()");

        loadBaseModel();
        loadMetaModel();
        loadScalerParams();
    }

    public bool SessionLoaded(string session)
    {
        var result = false;
        switch (session.ToLower().Trim())
        {
            case "meta":
            result = metaSession != null;
            break;
            default:
            result = baseSession != null;
            break;
        }

        if (!result)
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Evaluator.SessionLoaded {session} : {result}");
        return result;
    }

    public double[] ComputeFeatures(
        DateTime barTime,
         double atr14,
         double volumeAccel,
         double sessionHigh,
         double sessionLow,
         int tickCountThisBar,
         List<Bar> bars)
    {
        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Evaluator.ComputeFeatures() ");

        var f = new double[FeatureCount];
        var sessionPhase = computerSessionPhase(barTime);
        f[5] = sessionPhase;
        f[0] = atr14 * sessionPhase;
        
        var range = bars[0].High - bars[0].Low;
        var body = Math.Abs(bars[0].Close - bars[0].Open);
        f[1] = body / (range + 0.0001);

        var sessionRange = sessionHigh - sessionLow;
        f[2] = sessionRange > 0 ? (sessionHigh - bars[0].Close) / sessionRange : 0.0;
        f[3] = computeZScore(ZScoreLookback, bars);
        if (bars[ReturnLookback].Close > 0)
        {
            Console.WriteLine("bars[ReturnLookback].Close > 0");
            f[4] = (bars[0].Close - bars[ReturnLookback].Close) / bars[ReturnLookback].Close;
        }
        else
        {
            Console.WriteLine("bars[ReturnLookback].Close <= 0 returning false");
            return null;   
        }
        
        f[6] = tickCountThisBar;
        f[7] = volumeAccel;

        Console.WriteLine($"F : {string.Join(",", f)}");
        return f;
    }

    public double[] ScaleFeatures(double[] raw)
    {
        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Evaluator.ScaleFeatures() ");

        var scaled = new double[FeatureCount];
        for (var i = 0; i < FeatureCount; i++)
        {
            var s = scalerScale[i];
            if (Math.Abs(s) < 1e-15)
                scaled[i] = 0;
            else
                scaled[i] = (raw[i] - scalerMean[i]) / s;
        }
        Console.WriteLine($"scaled : {string.Join(",", scaled)}");
        return scaled;
    }

    public double RunBaseInterference(double[] features)
    {
        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Evaluator.RunBaseInterference() ");

        if (baseSession == null)
        {
            Console.WriteLine("BaseSession = null");
            return 0;   
        }

        try
        {
            var dims = new int[] { 1, FeatureCount };
            var inputTensor = new DenseTensor<float>(dims);
            for (var i = 0; i < FeatureCount; i++)
            {
                inputTensor[0, i] = (float) features[i];
            }

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(baseInputName, inputTensor)
            };
            using var results = baseSession.Run(inputs);

            return extractClassOneProbability(results);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Exception from RunBaseInterference : {ex.Message}");
        }

        return 0.0;
    }

    public double RunMetaInference(double[] scaled, double baseProbability)
    {
        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Evaluator.RunMetaInference() ");

        if (metaSession == null)
        {
            Console.WriteLine("MetaSession = null");
            return 0;
        }

        try
        {
            var dims= new int[] { 1, MetaFeatureCount };
            var inputTensor = new DenseTensor<float>(dims);
            for (var i = 0; i < FeatureCount; i++)
            {
                inputTensor[0, i] = (float) scaled[i];
            }

            inputTensor[0, FeatureCount] = (float) baseProbability;
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(metaInputName, inputTensor),
            };

            using var results = metaSession.Run(inputs);
            return extractClassOneProbability(results);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Exception from RunMetaInference : {ex.Message}");
        }

        return 0;
    }

    public void Dispose()
    {
        if (baseSession != null)
        baseSession.Dispose();
        if (metaSession != null)
        metaSession.Dispose();
    }

    private void loadBaseModel()
    {
        try
        {
            baseSession = new InferenceSession(ModelPath);
            baseInputName = baseSession.InputMetadata.Keys.First();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Exception from loadBaseModel : {ex.Message}");
            baseSession = null;
        }
    }

    private void loadMetaModel()
    {
        try
        {
            metaSession = new InferenceSession(MetaModelPath);
            metaInputName = metaSession.InputMetadata.Keys.First();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Exception from loadMetaModel : {ex.Message}");
            metaSession = null;
        }
    }

    private void loadScalerParams()
    {
        scalerMean = new double[FeatureCount];
        scalerScale = new double[FeatureCount];
        ScalerLoaded = false;

        for (var i = 0; i < FeatureCount; i++)
        {
            scalerMean[i] = 0;
            scalerScale[i] = 1;
        }

        if (string.IsNullOrEmpty(ScalerPath) || !File.Exists(ScalerPath))
        {
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - loadScalerParams scalerPath = null | missing");
            return;
        }

        try
        {
            var lines = File.ReadAllLines(ScalerPath);
            if (lines.Length < 3)
            {
                // TODO: LOGGING
                return;
            }

            var names = lines.First().Split(',');
            if (names.Length != FeatureCount)
            {
                // TODO: LOGGING
                return;
            }
            var meanParts = lines[1].Split(',');
            var scaleParts = lines[2].Split(',');

            if (meanParts.Length != FeatureCount || scaleParts.Length != FeatureCount)
            {
                Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - meanParts/scalerParts count != Feature Count, {meanParts.Length}, {scaleParts.Length}, {FeatureCount}");
                return;
            }

            for (var i = 0; i < FeatureCount; i++)
            {
                scalerMean[i] = double.Parse(meanParts[i].Trim(), CultureInfo.InvariantCulture);
                scalerScale[i] = double.Parse(scaleParts[i].Trim(), CultureInfo.InvariantCulture);
            }

            ScalerLoaded = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Exception from loadScalerParams : {ex.Message}");
        }
    }

    private double computerSessionPhase(DateTime barTime)
    {
        DateTime sessionStart;
        DateTime sessionEnd;
        TimeSpan tod = barTime.TimeOfDay;

        if (tod >= new TimeSpan(17, 0, 0))
        {
            sessionStart = barTime.Date.AddHours(17);
            sessionEnd = barTime.Date.AddDays(-1).AddHours(16);
        }
        else if (tod < new TimeSpan(16, 0, 0))
        {
            sessionStart = barTime.Date.AddDays(-1).AddHours(17);
            sessionEnd = barTime.Date.AddHours(16);
        }
        else
        {
            return 1.0;
        }

        var totalMinutes = (sessionEnd - sessionStart).TotalMinutes;
        var elapsedMinutes = (barTime - sessionStart).TotalMinutes;

        if (totalMinutes <= 0)
        {
            return 0.5;
        }
        var phase = elapsedMinutes / totalMinutes;
        return Math.Max(0.0, Math.Min(1.0, phase));
    }

    private double computeZScore(int lookback, List<Bar> bars)
    {
        var sum = 0.0;
        for (var i = 0; i < lookback; i++)
        {
            sum += bars[i].Close;
        }

        var mean = sum / lookback;
        var sumSq = 0.0;
        for (var i = 0; i < lookback; i++)
        {
            var diff = bars[i].Close - mean;
            sumSq += diff * diff;
        }
        var std = Math.Sqrt(sumSq / lookback);
        if (std < 1e-10)
            return 0;
        
        return (bars[0].Close - mean) / std;
    }

    private double extractClassOneProbability(IReadOnlyCollection<DisposableNamedOnnxValue> results)
    {
        var tryIndices = results.Count >= 2 ? new int[] { 1, 0 } : new int[] { 0 };
        foreach (var idx in tryIndices)
        {
            var output = results.ElementAt(idx);
            var sequence = output.Value as IEnumerable<NamedOnnxValue>;
            if (sequence != null)
            {
                var firstItem = sequence.FirstOrDefault();
                if (firstItem != null)
                {
                    var mapDict = firstItem.Value as IDictionary<long, float>;
                    if (mapDict != null)
                    {
                        float prob;
                        if (mapDict.TryGetValue(1L, out prob))
                            return prob;
                    }
                }
            }
            var seqMap = output.Value as IEnumerable<IDictionary<long, float>>;
            if (seqMap != null)
            {
                var classProbs = seqMap.FirstOrDefault();
                if (classProbs != null)
                {
                    float prob;
                    if (classProbs.TryGetValue(1L, out prob))
                    return prob;
                }
            }

            var tensor = output.Value as DenseTensor<float>;
            if (tensor != null && tensor.Length >= 1)
            {
                if (tensor.Dimensions.Length == 2 && tensor.Dimensions[1] >= 2)
                    return tensor[0, 1];
                return tensor[0];
            }
        }

        return 0;
    }
}