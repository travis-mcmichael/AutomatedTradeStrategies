using AutomatedTrading.Core.Api.Models;
using AutomatedTrading.Core.Api.Models.Evaluate;
using AutomatedTrading.Core.Framework.Enums;
using AutomatedTrading.Core.Framework.Logging;

namespace AutomatedTrading.Core.Api.Endpoints;

public static class SessionLowEndpoints
{
    private static readonly string SESSION_START = "/strategy/session-low/session";
    private static readonly string EVAL_BAR = "/strategy/session-low/bar";
    public static void Map(WebApplication app)
    {
        app.MapPost(SESSION_START, async (
            SessionLowSessionRequest req,
            SessionLowStrategyEvaluator evaluator) => 
        {
            Logger.Info($"Endpoint {SESSION_START} called.");
            if (string.IsNullOrWhiteSpace(req.SessionDate))
                return Results.BadRequest("SessionDate is required"); 
            await evaluator.StartSessionAsync(
                req.SessionDate,
                req.SessionOpen,
                req.Proximity,
                req.StopLossPoints,
                req.TargetProfit,
                req.Trail);
            return Results.Ok(new SessionLowSessionResponse(
                evaluator.IsReady,
                evaluator.PredictedLow,
                evaluator.DailyAtr));
        });
        app.MapPost(EVAL_BAR, (
            SessionLowBarRequest req,
            SessionLowStrategyEvaluator evaluator) =>
        {
            Logger.Info($"Endpoint {EVAL_BAR} called.");
            var position = req.Position switch
            {
                "Long" => PositionSide.Long,
                "Short" => PositionSide.Short,
                _ => PositionSide.Flat,
            };
            var eval = evaluator.Evaluate(req.Low, req.Close, position, req.EntryPrice);
            return Results.Ok(new SessionLowBarResponse(eval.Signal.ToString(), eval.StopPrice));
        });
    }
}