using AutomatedTrading.Core.Models;

namespace AutomatedTradeStrategies.Management.Web.Models;

// Body of POST /Home/UpdateFeatureParams: the complete set of param values for one model's features,
// plus each feature's (optional) name.
public class ModelFeatureParamsUpdate
{
    public long ModelConfigId { get; set; }
    public List<ModelFeatureParam> Params { get; set; } = [];
    public List<ModelFeatureNameUpdate> Features { get; set; } = [];
}

public class ModelFeatureNameUpdate
{
    public long ModelFeatureId { get; set; }
    public string? ModelFeatureName { get; set; }
}
