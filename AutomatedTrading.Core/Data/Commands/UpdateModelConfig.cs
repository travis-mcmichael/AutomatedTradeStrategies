using System.Threading.Tasks;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Data.Commands;

public class UpdateModelConfig(DeleteModelParameters deleteModelParameters,
    IDefaultInsertCommand<ModelConfig> insertModel,
    IDefaultUpdateCommand<ModelConfig> updateModel,
    IDefaultDeleteCommand<ModelFeature> deleteFeatures,
    IDefaultInsertCommand<ModelFeature> insertFeatures,
    IDefaultInsertCommand<ModelFeatureParam> insertFeatureParameters
)
{
    public async Task<long> Execute(ModelConfig modelConfig)
    {
        await deleteModelParameters.Execute(modelConfig);
        await deleteFeatures.Execute(new { ModelConfigId = modelConfig.Id });

        if (modelConfig.Id is 0)
        {
            modelConfig.Id = await insertModel.Execute(modelConfig);
        }
        else
        {
            await updateModel.Execute(modelConfig);
        }

        foreach (var feature in modelConfig.ModelFeatures)
        {
            feature.ModelConfigId = modelConfig.Id;
            feature.Id = await insertFeatures.Execute(feature);
            foreach (var parameter in feature.ModelFeatureParams)
            {
                parameter.ModelFeatureId = feature.Id;
                await insertFeatureParameters.Execute(parameter);
            }
        }

        return modelConfig.Id;
    }
}