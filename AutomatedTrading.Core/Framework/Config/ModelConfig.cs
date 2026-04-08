using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AutomatedTrading.Core.Framework.Enums;
using AutomatedTrading.Core.Framework.Utility;

namespace AutomatedTrading.Core.Framework.Config;

public record ModelConfig
{
    public List<ModelEntry> Models { get; }
    public ModelConfig(List<ModelEntry> models)
    {
        Models = models;
    }
    public static ModelConfig? GetConfig()
    {
        var configPath = Operations.NormalizePath(AppConstants.MODELS_CONFIG_PATH);
        var json = File.ReadAllText(configPath);
        return JsonSerializer.Deserialize<ModelConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
}

public class ModelEntry{
    public string ModelName { get; }
    public string Version { get; }
    public string Instrument { get; }
    public string TrainingStart { get; }
    public string TrainingEnd { get; }
    public PredictionTarget Target { get; }
    public string OutputLocation { get; }
    public string FeatureLocation { get; }
    public string BarType { get; }
    public string? WindowStart { get; set; }
    public string? WindowEnd { get; set; }
    public bool Active { get; }
    public bool Export { get; }
    public List<ModelFeature> Features { get; }
    public ModelEntry(
        string ModelName,
        string Version,
        string Instrument,
        string TrainingStart,
        string TrainingEnd,
        PredictionTarget Target,
        string OutputLocation,
        string FeatureLocation,
        string BarType,
        string? WindowStart,
        string? WindowEnd,
        bool Active,
        bool Export,
        List<ModelFeature> Features)
    {
        this.ModelName = ModelName;
        this.Version = Version;
        this.Instrument = Instrument;
        this.TrainingStart = TrainingStart;
        this.TrainingEnd = TrainingEnd;
        this.Target = Target;
        this.OutputLocation = OutputLocation;
        this.FeatureLocation = FeatureLocation;
        this.BarType = BarType;
        this.WindowStart = WindowStart;
        this.WindowEnd = WindowEnd;
        this.Active = Active;
        this.Export = Export;
        this.Features = Features;
    }
}

public class ModelFeature
{
    public string Name { get; }
    public string Source { get; }
    public Dictionary<string, int>? Params { get; }
    public ModelFeature(
    string Name,
    string Source = "daily",
    Dictionary<string, int>? Params = null)
    {
        this.Name = Name;
        this.Source = Source;
        this.Params = Params;
    }
}
