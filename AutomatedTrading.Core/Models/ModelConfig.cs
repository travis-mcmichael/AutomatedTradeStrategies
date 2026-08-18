using System.Collections.Generic;
using AutomatedTrading.Core.Data;

namespace AutomatedTrading.Core.Models;

public class ModelConfig : IDatabaseEntity
{
    public string TableName { get; } = "ModelConfig";
    public string SchemaName { get; } = "dbo";
    public long Id { get; set; }
    public string ModelName { get; set; } = default!;
    public string Version { get; set; } = default!;
    public string Instrument { get; set; } = default!;
    public string TrainingStart { get; set; } = default!;
    public string TrainingEnd { get; set; } = default!;
    public string Target { get; set; } = default!;
    public string BarType { get; set; } = default!;
    public string? WindowStart { get; set; }
    public string? WindowEnd { get; set; }
    public string FeatureLocation { get; set; } = default!;
    public string OutputLocation { get; set; } = default!;
    public bool Export { get; set; }
    public bool Active { get; set; }
    public List<ModelFeature> ModelFeatures { get; set; } = [];
}
