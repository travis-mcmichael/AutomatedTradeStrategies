using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Data.Queries;

public class GetFeatureTypes(IDefaultRepository<FeatureType> featureTypes,
    IDefaultRepository<FeatureTypeParam> parameters)
{
    public async Task<IEnumerable<FeatureType>> Execute()
    {
        var features = await featureTypes.GetDataAsync(new Dictionary<string, object> { { "1", 1 } });
        var prms = await parameters.GetDataAsync(new Dictionary<string, object> { { "1", 1 } });
        foreach (var feature in features)
        {
            feature.FeatureTypeParams = prms.Where(x => x.FeatureTypeId == feature.Id);
        }
        return features;
    }
}