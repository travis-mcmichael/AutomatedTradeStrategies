using AutomatedTrading.Core.Api.Models;
using AutomatedTrading.Core.Strategies;
using AutomatedTrading.Core.Framework.Enums;

namespace AutomatedTrading.Core.Api.Endpoints;

public static class SessionHighEndpoints
{
    public static void Map(WebApplication app)
    {
        // Called once at RTH session open. Runs ML inference, loads daily ATR,
        // and arms the evaluator for the session.
        app.MapPost("/strategy/session-high/session", async (
            SessionHighSessionRequest req,
            SessionHighStrategyEvaluator evaluator) =>
        {
            if (string.IsNullOrWhiteSpace(req.SessionDate))
                return Results.BadRequest("SessionDate is required.");

            await evaluator.StartSessionAsync(
                req.SessionDate,
                req.SessionOpen,
                req.Proximity,
                req.StopLossPoints,
                req.TargetProfit,
                req.Trail);

            return Results.Ok(new SessionHighSessionResponse(
                evaluator.IsReady,
                evaluator.PredictedHigh,
                evaluator.DailyAtr));
        });

        // Called on every bar close. Returns the signal the strategy should act on.
        app.MapPost("/strategy/session-high/bar", (
            SessionHighBarRequest req,
            SessionHighStrategyEvaluator evaluator) =>
        {
            var position = req.Position switch
            {
                "Long" => PositionSide.Long,
                "Short" => PositionSide.Short,
                _ => PositionSide.Flat,
            };

            var eval = evaluator.Evaluate(req.High, req.Close, position, req.EntryPrice);
            return Results.Ok(new SessionHighBarResponse(eval.Signal.ToString(), eval.StopPrice));
        });
    }
}
