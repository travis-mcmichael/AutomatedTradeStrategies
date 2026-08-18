using AutomatedTrading.Core.Data;

namespace AutomatedTrading.Core.Models;

public class FeatureTypeParam : IDatabaseEntity
{
    public string TableName { get; } = "FeatureTypeParam";
    public string SchemaName { get; } = "dbo";
    public long Id { get; set; }
    public long FeatureTypeId { get; set; }
    public string ParameterKey { get; set; } = default!;
}