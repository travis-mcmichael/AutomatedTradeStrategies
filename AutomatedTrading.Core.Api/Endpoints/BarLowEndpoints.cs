using AutomatedTrading.Core.Api.Models;
using AutomatedTrading.Core.Api.Services;

namespace AutomatedTrading.Core.Api.Endpoints;

public static class BarLowEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/inference/bar-low", async (BarInferenceRequest req, BarInferenceService svc) =>
        {
            if (string.IsNullOrWhiteSpace(req.SessionDate))
                return Results.BadRequest("SessionDate is required.");

            var result = await svc.RunLowInference(req.SessionDate, req.SessionOpen);

            return result is null
                ? Results.NotFound($"Insufficient data to run inference for session {req.SessionDate}.")
                : Results.Ok(result);
        });
    }
}
