using System.Collections.Generic;
using AutomatedTrading.Core.Data;

namespace AutomatedTrading.Core.Models;

public class FeatureType : IDatabaseEntity
{
    public string TableName { get; } = "FeatureType";
    public string SchemaName { get; } = "dbo";
    public long Id { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public bool CanUseWindow { get; set; }
    public IEnumerable<FeatureTypeParam> FeatureTypeParams { get; set; } = [];
}