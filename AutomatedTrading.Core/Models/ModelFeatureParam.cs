namespace AutomatedTrading.Core.Models;

public class ModelFeatureParam
{
    public string TableName { get; } = "ModelFeatureParam";
    public string Schema { get; } = "dbo";
    public long Id { get; set; }
    public string ParamKey { get; set; } = default!;
    public int ParamValue { get; set; }
}
