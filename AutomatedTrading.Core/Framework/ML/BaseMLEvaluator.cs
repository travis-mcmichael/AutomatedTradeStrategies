using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace AutomatedTrading.Core.Framework.ML;

public class BaseMLEvaluator : IDisposable
{
    private readonly string modelPath;
    private readonly string scalerPath;
    private readonly int featureCount;
    private InferenceSession? session;
    private string? inputName;
    private double[]? scalerMean;
    private double[]? scalerScale;
    public bool ScalerLoaded { get; private set; }

    public BaseMLEvaluator(string modelPath, string scalerPath, int featureCount)
    {
        this.modelPath    = modelPath;
        this.scalerPath   = scalerPath;
        this.featureCount = featureCount;
    }

    public void LoadModel()
    {
        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - BaseMLEvaluator.LoadModel()");
        loadXgbModel();
        loadScalerParameters();
    }

    /// <summary>
    /// Applies standard scaling (z-score normalization) to the raw feature array using
    /// the loaded scaler parameters. Returns the original array unchanged if the scaler
    /// was not loaded.
    /// </summary>
    public double[] Scale(double[] features)
    {
        if (!ScalerLoaded)
            return features;

        var scaled = new double[features.Length];
        for (int i = 0; i < features.Length; i++)
            scaled[i] = scalerScale![i] == 0 ? 0 : (features[i] - scalerMean![i]) / scalerScale[i];

        return scaled;
    }

    public double RunInference(double[] features)
    {
        if (session == null)
        {
            Console.WriteLine("Session not loaded");
            return 0.0;
        }

        if (features.Length != featureCount)
        {
            Console.WriteLine($"Feature count mismatch: expected {featureCount}, got {features.Length}");
            return 0.0;
        }

        var dims = new[] { 1, features.Length };
        var inputTensor = new DenseTensor<float>(dims);
        for (var i = 0; i < features.Length; i++)
            inputTensor[0, i] = (float)features[i];

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(inputName, inputTensor)
        };
        using var results = session.Run(inputs);
        return extractOutput(results);
    }

    public bool SessionLoaded() => session != null;

    public void Dispose()
    {
        session?.Dispose();
        GC.SuppressFinalize(this);
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
            session   = new InferenceSession(modelPath);
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
        if (!File.Exists(scalerPath))
        {
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Scaler file missing: {scalerPath}");
            return;
        }

        if (ScalerLoaded)
        {
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Scaler loading skipped (already loaded)");
            return;
        }

        try
        {
            var jsonText = File.ReadAllText(scalerPath);
            var scalerData = System.Text.Json.JsonDocument.Parse(jsonText).RootElement;
            scalerMean = [.. scalerData.GetProperty("mean").EnumerateArray().Select(x => x.GetDouble())];
            scalerScale = [.. scalerData.GetProperty("scale").EnumerateArray().Select(x => x.GetDouble())];

            if (scalerMean.Length != featureCount || scalerScale.Length != featureCount)
            {
                Console.WriteLine($"Scaler length mismatch: expected {featureCount}, got mean={scalerMean.Length} scale={scalerScale.Length}");
                return;
            }

            ScalerLoaded = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Exception loading scaler: {ex.Message}");
        }
    }

    private static double extractOutput(IReadOnlyCollection<DisposableNamedOnnxValue> results)
    {
        var output = results.FirstOrDefault();
        if (output?.Value is DenseTensor<float> tensor)
            return tensor[0];

        return 0.0;
    }
}
