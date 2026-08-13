using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Data.Queries;

public class GetAllModelConfigs(IDefaultRepository<ModelConfig> modelConfigs,
    IDefaultRepository<ModelFeature> modelFeatures,
    IDefaultRepository<ModelFeatureParam> modelFeatureParams,
    IDefaultRepository<FeatureType> featureTypes,
    IDefaultRepository<FeatureTypeParam> featureTypeParams)
{
    public async Task<IList<ModelConfig>> Execute()
    {
        var parameters = await modelFeatureParams.GetDataAsync(new Dictionary<string, object> { { "1", 1 } });
        var features = await modelFeatures.GetDataAsync(new Dictionary<string, object> { { "1", 1 } });
        var models = await modelConfigs.GetDataAsync(new Dictionary<string, object> { { "1", 1 } });
        var fTypes = await featureTypes.GetDataAsync(new Dictionary<string, object> { { "1", 1 } });
        var fTypeParams = await featureTypeParams.GetDataAsync(new Dictionary<string, object> { { "1", 1 } });

        foreach (var parameter in parameters)
        {
            parameter.FeatureTypeParam = fTypeParams.FirstOrDefault(x => x.Id == parameter.FeatureTypeParamId);
        }
        foreach (var type in fTypes)
        {
            type.FeatureTypeParams = fTypeParams.Where(x => x.FeatureTypeId == type.Id);
        }
        foreach (var model in models)
        {
            foreach (var feature in features)
            {
                feature.ModelFeatureParams = [.. parameters.Where(x => x.ModelFeatureId == feature.Id)];
                feature.FeatureType = fTypes.FirstOrDefault(x => x.Id == feature.FeatureTypeId);
            }
            model.ModelFeatures = [.. features.Where(x => x.ModelConfigId == model.Id)];
        }
        return [.. models];
    }
}