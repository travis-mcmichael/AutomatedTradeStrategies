using AutomatedTrading.Core.Models;

namespace AutomatedTradeStrategies.Management.Web.Models;

// Body of POST /Home/UpdateFeatureParams: the complete set of param values for one model's features.
public class ModelFeatureParamsUpdate
{
    public long ModelConfigId { get; set; }
    public List<ModelFeatureParam> Params { get; set; } = [];
}
