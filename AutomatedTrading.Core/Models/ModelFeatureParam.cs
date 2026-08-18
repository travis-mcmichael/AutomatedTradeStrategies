using AutomatedTrading.Core.Data;

namespace AutomatedTrading.Core.Models;

public class ModelFeatureParam : IDatabaseEntity
{
    public string TableName { get; } = "ModelFeatureParam";
    public string SchemaName { get; } = "dbo";
    public long Id { get; set; }
    public long ModelFeatureId { get; set; }
    public long FeatureTypeParamId { get; set; }
    public FeatureTypeParam? FeatureTypeParam { get; set; }
    public int ParamValue { get; set; }
}
