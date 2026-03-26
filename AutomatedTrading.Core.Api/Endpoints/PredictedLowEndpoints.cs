
using AutomatedTrading.Core.Api.Models;
using AutomatedTrading.Core.Framework.Enums;
using AutomatedTrading.Core.Strategies;

namespace AutomatedTrading.Core.Api.Endpoints;

public static class PredictedLowEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/strategy/predicted-low/session", async (
            PredictedLowSessionRequest req,
            PredictedLowStrategyEvaluator evaluator) => 
        {
            if (string.IsNullOrWhiteSpace(req.SessionDate))
                return Results.BadRequest("SessionDate is required"); 
            await evaluator.StartSessionAsync(
                req.SessionDate,
                req.SessionOpen,
                req.Proximity,
                req.StopLossPoints,
                req.TargetProfit,
                req.Trail);
            return Results.Ok(new PredictedLowSessionResponse(
                evaluator.IsReady,
                evaluator.PredictedLow,
                evaluator.DailyAtr));
        });
        app.MapPost("/strategy/predicted-low/bar", (
            PredictedLowBarRequest req,
            PredictedLowStrategyEvaluator evaluator) =>
        {
            var position = req.Position switch
            {
                "Long" => PositionSide.Long,
                "Short" => PositionSide.Short,
                _ => PositionSide.Flat,
            };
            var eval = evaluator.Evaluate(req.Low, req.Close, position, req.EntryPrice);
            return Results.Ok(new PredictedLowBarResponse(eval.Signal.ToString(), eval.StopPrice));
        });
    }
}