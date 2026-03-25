using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace AutomatedTrading.Core.Api.Evaluators;

public class BaseMLEvaluator : IDisposable
{
    // should the paths be in a config?
    private string model = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models\\20260319", "xgb_model.onnx");
    private string scaler = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models\\20260319", "scaler_params.json");
    private InferenceSession? session;
    private string? inputName;
    private int featureCount = 34;
    private double[]? scalerMean;
    private double[]? scalerScale;
    public bool ScalerLoaded = false;

    public BaseMLEvaluator(string model, string scaler, int featureCount)
    {
        this.model = model;
        this.scaler = scaler;
        this.featureCount = featureCount;
    }

    public void LoadModel()
    {
        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - BaseMLEvaluator.LoadModel()");
        loadXgbModel();
        loadScalerParameters();
    }

    public double RunInference(double[] features)
    {
        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - BaseMLEvaluator.LoadModel()");
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

    public bool SessionLoaded()
    {
        return session != null;
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
            session = new InferenceSession(model);
            inputName = session.InputMetadata.Keys.First();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Exception loading model: {ex.Message}");
            session = null;
        }
    }

    private void loadScalerParameters()
    {
        if (!File.Exists(scaler))
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
            var jsonText = File.ReadAllText(scaler);
            var scalerData = System.Text.Json.JsonDocument.Parse(jsonText).RootElement;
            scalerMean = [.. scalerData.GetProperty("mean").EnumerateArray().Select(x => x.GetDouble())];
            scalerScale = [.. scalerData.GetProperty("scale").EnumerateArray().Select(x => x.GetDouble())];
            if (scalerMean.Length != featureCount || scalerScale.Length != featureCount)
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
        if (output?.Value is DenseTensor<float> tensor1d)
        return tensor1d[0];
            //return Math.Clamp(tensor1d[0], 0, 3);

        return 0.0;
    }
}
