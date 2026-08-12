using System.Collections.Generic;
using AutomatedTrading.Core.Data;

namespace AutomatedTrading.Core.Models;

public class ModelFeature : IDatabaseEntity
{
    public string TableName { get; } = "ModelFeature";
    public string SchemaName { get; } = "dbo";
    public long Id { get; set; }
    public string Name { get; set; } = default!;
    public string? Source { get; set; }
    public int SortOrder { get; set; }
    public List<ModelFeatureParam> ModelFeatureParams { get; set; }
}
