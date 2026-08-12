using AutomatedTrading.Core.Data;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Api.Endpoints;

public static class ModelEndpoints
{
    private static readonly string GET_MODELS = "/models";
    private static readonly string SAVE_MODEL = "/models";

    public static void Map(WebApplication app)
    {
        // Returns every ModelConfig row, with its Features and each feature's Params fully populated.
        _ = app.MapGet(GET_MODELS, async (
            IDefaultRepository<ModelConfig> repository) =>
        {
            var models = await repository.GetDataAsync(new Dictionary<string, object> { { "1", "1" } });
            return Results.Ok(models);
        });

        // Inserts a new ModelConfig, or updates the existing one matched by ModelName.
        // Features/Params are always replaced wholesale with what's in the request.
        app.MapPost(SAVE_MODEL, async (
            ModelConfig req,
            IModelConfigRepository repository) =>
        {
            if (string.IsNullOrWhiteSpace(req.ModelName))
                return Results.BadRequest("ModelName is required.");

            var saved = await repository.UpsertAsync(req);
            return Results.Ok(saved);
        });
    }
}
