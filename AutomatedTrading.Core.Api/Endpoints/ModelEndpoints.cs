using AutomatedTrading.Core.Data;
using AutomatedTrading.Core.Data.Commands;
using AutomatedTrading.Core.Data.Queries;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Api.Endpoints;

public static class ModelEndpoints
{
    private static readonly string GET_MODELS = "/models";
    private static readonly string SAVE_MODEL = "/models";
    private static readonly string SAVE_PERFORMANCE = "/models/perf";

    public static void Map(WebApplication app)
    {
        // Returns every ModelConfig row, with its Features and each feature's Params fully populated.
        _ = app.MapGet(GET_MODELS, async (
            GetAllModelConfigs getAllModels) =>
        {
            var models = await getAllModels.Execute();
            return Results.Ok(models);
        });

        // Inserts a new ModelConfig, or updates the existing one matched by ModelName.
        // Features/Params are always replaced wholesale with what's in the request.
        app.MapPost(SAVE_MODEL, async (
            ModelConfig req,
            UpdateModelConfig updateModelConfig) =>
        {
            if (string.IsNullOrWhiteSpace(req.ModelName))
                return Results.BadRequest("ModelName is required.");
            return Results.Ok(await updateModelConfig.Execute(req));
        });

        app.MapPost(SAVE_PERFORMANCE, async (
            ModelPerformance req,
            IDefaultInsertCommand<ModelPerformance> insert,
            IDefaultUpdateCommand<ModelPerformance> update
        ) =>
        {
            if (req.Id is 0)
            {
                await insert.Execute(req);
            }
            else
            {
                await update.Execute(req);
            }
        });
    }
}
