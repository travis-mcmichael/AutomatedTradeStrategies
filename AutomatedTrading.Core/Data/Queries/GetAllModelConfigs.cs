using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Data.Queries;

public class GetAllModelConfigs(IDefaultRepository<ModelConfig> modelConfigs,
    IDefaultRepository<ModelFeature> modelFeatures,
    IDefaultRepository<ModelFeatureParam> modelFeatureParams,
    IDefaultRepository<ModelPerformance> modelPerformance,
    IDefaultRepository<ModelPerformanceSplit> modelPerformanceSplits,
    IDefaultRepository<FeatureType> featureTypes,
    IDefaultRepository<FeatureTypeParam> featureTypeParams)
{
    public async Task<IList<ModelConfig>> Execute()
    {
        var parameters = await modelFeatureParams.GetDataAsync(new Dictionary<string, object>());
        var features = await modelFeatures.GetDataAsync(new Dictionary<string, object>());
        var models = await modelConfigs.GetDataAsync(new Dictionary<string, object>());
        var fTypes = await featureTypes.GetDataAsync(new Dictionary<string, object>());
        var fTypeParams = await featureTypeParams.GetDataAsync(new Dictionary<string, object>());
        var performances = await modelPerformance.GetDataAsync(new Dictionary<string, object>());
        var splits = await modelPerformanceSplits.GetDataAsync(new Dictionary<string, object>());

        foreach (var parameter in parameters)
        {
            parameter.FeatureTypeParam = fTypeParams.FirstOrDefault(x => x.Id == parameter.FeatureTypeParamId);
        }
        foreach (var type in fTypes)
        {
            type.FeatureTypeParams = fTypeParams.Where(x => x.FeatureTypeId == type.Id);
        }

        var splitsByPerformance = splits.GroupBy(s => s.ModelPerformanceId).ToDictionary(g => g.Key, g => g.OrderBy(s => s.Id).ToList());
        foreach (var performance in performances)
        {
            performance.Splits = splitsByPerformance.TryGetValue(performance.Id, out var performanceSplits) ? performanceSplits : [];
        }

        foreach (var model in models)
        {
            foreach (var feature in features)
            {
                feature.ModelFeatureParams = [.. parameters.Where(x => x.ModelFeatureId == feature.Id)];
                feature.FeatureType = fTypes.FirstOrDefault(x => x.Id == feature.FeatureTypeId);
            }
            model.ModelFeatures = [.. features.Where(x => x.ModelConfigId == model.Id)];
            // One performance row per trained version; most recent training run first.
            model.ModelPerformances = [.. performances
                .Where(x => x.ModelConfigId == model.Id)
                .OrderByDescending(x => x.TrainedAt)
                .ThenByDescending(x => x.Id)];
        }
        return [.. models];
    }
}
