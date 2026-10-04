using AutomatedTrading.Core.Models;

namespace AutomatedTradeStrategies.Management.Web.Models;

// Body of POST /Home/UpdateFeatureParams: the complete set of param values for one model's features,
// each feature's (optional) name, and the features to delete.
public class ModelFeatureParamsUpdate
{
    public long ModelConfigId { get; set; }
    public List<ModelFeatureParam> Params { get; set; } = [];
    public List<ModelFeatureNameUpdate> Features { get; set; } = [];
    public List<long> DeletedFeatureIds { get; set; } = [];
}

public class ModelFeatureNameUpdate
{
    public long ModelFeatureId { get; set; }
    public string? ModelFeatureName { get; set; }
}
