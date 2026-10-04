using System.Collections.Generic;
using AutomatedTrading.Core.Data;

namespace AutomatedTrading.Core.Models;

public class ModelFeature : IDatabaseEntity
{
    public string TableName { get; } = "ModelFeature";
    public string SchemaName { get; } = "dbo";
    public long Id { get; set; }
    public long FeatureTypeId { get; set; }
    public FeatureType? FeatureType { get; set; }
    public string? Source { get; set; }
    public long ModelConfigId { get; set; }
    public int SortOrder { get; set; }
    public string? ModelFeatureName { get; set; }
    public IEnumerable<ModelFeatureParam> ModelFeatureParams { get; set; } = [];
}
