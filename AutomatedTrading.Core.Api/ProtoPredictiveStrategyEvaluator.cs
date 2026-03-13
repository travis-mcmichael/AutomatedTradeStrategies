using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

public class ProtoPredictiveEvaluator : IDisposable
{
    readonly string ModelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NeuralNetwork\\20260309", "xgb_model.onnx");
    readonly string ScalerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NeuralNetwork\\20260309", "scaler_params.json");

    // =====================================================================
    // Constants
    // =====================================================================
    private const int FeatureCount = 13;  // Update to match your flattened feature vector
    private string? inputName;
    private InferenceSession? session;
    private double[]? scalerMean;
    private double[]? scalerScale;
    public bool ScalerLoaded = false;

    static int sequenceLength = 100;
    static int predictionHorizon = 20;

    public void LoadModel()
    {
        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - ProtoPredictiveEvaluator.LoadModel()");    
        loadXgbModel();
        loadScalerParams();

    }

    public bool SessionLoaded()
    {
        return session != null;
    }

    public double[] ScaleFeatures(double[] raw)
    {
        if (!ScalerLoaded || scalerMean == null || scalerScale == null)
            return raw;
        
        var scaled = new double[FeatureCount * sequenceLength];
        for (var i = 0; i < FeatureCount * sequenceLength; i++)
        {
            var s = scalerScale[i];
            if (Math.Abs(s) > 1e-15)
                scaled[i] = 0;
            else scaled[i] = (raw[i] - scalerMean[i]) / s;
        }
        return scaled;
    }

    public double RunInference(double[] features)
    {
        if (session == null)
        {
            Console.WriteLine("Session not loaded");
            return 0.0;
        }

        var dims = new int[] { 1, features.Length };
        var inputTensor = new DenseTensor<float>(dims);
        for (var i = 0; i < features.Length; i++)
        {
            inputTensor[0, i] = (float) features[i];
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(inputName, inputTensor)
        };
        using var results = session.Run(inputs);
        return extractClassOneProbability(results);
    }

    public void Dispose()
    {
        session?.Dispose();
    }

    private void loadXgbModel()
    {
        if (SessionLoaded())
        {
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - XGB model load skipped (already loaded)");
            return; 
        }

        try
        {
            session = new InferenceSession(ModelPath);
            inputName = session.InputMetadata.Keys.First();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Exception loading model: {ex.Message}");
            session = null;
        }
    }

    private void loadScalerParams()
    {
        if (!File.Exists(ScalerPath))
        {
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Scaler file missing");
            return;
        }

        if (ScalerLoaded == true)
        {
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Scaler loading skipped (already loaded)");
            return;
        }
        
        try
        {
            var jsonText = File.ReadAllText(ScalerPath);
            var scalerData = System.Text.Json.JsonDocument.Parse(jsonText).RootElement;
            scalerMean = [.. scalerData.GetProperty("mean").EnumerateArray().Select(x => x.GetDouble())];
            scalerScale = [.. scalerData.GetProperty("scale").EnumerateArray().Select(x => x.GetDouble())];
            if (scalerMean.Length != FeatureCount * sequenceLength || scalerScale.Length != FeatureCount * sequenceLength)
            {
                Console.WriteLine("Scaler length mismatch with FeatureCount");
                return;
            }
            ScalerLoaded = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Exception loading scaler: {ex.Message}");
        }
    }
    
    private double extractClassOneProbability(IReadOnlyCollection<DisposableNamedOnnxValue> results)
    {
        var output = results.FirstOrDefault();
        if (output?.Value is DenseTensor<float> tensor && tensor.Dimensions.Length == 2 && tensor.Dimensions[1] >= 2)
            return tensor[0, 1];
        if (output?.Value is DenseTensor<float> tensor1d && tensor1d.Dimensions.Length == 1)
            return tensor1d[0];

        return 0.0;
    }
}