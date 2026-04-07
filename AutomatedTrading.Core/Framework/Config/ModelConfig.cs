using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AutomatedTrading.Core.Framework.Utility;

namespace AutomatedTrading.Core.Framework.Config;

public record ModelConfig(List<ModelEntry> Models)
{
    public static ModelConfig? GetConfig()
    {
        var configPath = Operations.NormalizePath(AppConstants.MODELS_CONFIG_PATH);
        var json = File.ReadAllText(configPath);
        return JsonSerializer.Deserialize<ModelConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
}

public record ModelEntry(
    string ModelName,
    string Version,
    string Instrument,
    string TrainingStart,
    string TrainingEnd,
    string OutputLocation,
    string FeatureLocation,
    int FeatureCount,
    bool Active,
    bool Export,
    List<ModelFeature> Features
);

public record ModelFeature(
    string Name,
    string Source = "daily",
    Dictionary<string, int>? Params = null
);
