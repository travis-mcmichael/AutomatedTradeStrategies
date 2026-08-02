using System.Collections.Generic;

namespace AutomatedTrading.Core.Models;

public class ModelFeature
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
    public string? Source { get; set; }
    public int SortOrder { get; set; }
    public List<ModelFeatureParam> ModelFeatureParams { get; set; }
}
