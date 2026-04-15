using AutomatedTrading.Core.Api.Models;
using AutomatedTrading.Core.Api.Models.Evaluate;
using AutomatedTrading.Core.Framework.Enums;
using AutomatedTrading.Core.Framework.Logging;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Api.Endpoints;

public static class StrategyEndpoints
{
    private static readonly string SESSION_START = "/strategy/session";
    private static readonly string EVAL_BAR      = "/strategy/bar";

    public static void Map(WebApplication app)
    {
        // Called once at session open. Runs all active ML predictors, loads daily ATR,
        // and arms the evaluator for the session.
        app.MapPost(SESSION_START, async (
            StrategySessionRequest req,
            StrategyEvaluator evaluator) =>
        {
            if (string.IsNullOrWhiteSpace(req.SessionDate))
                return Results.BadRequest("SessionDate is required.");

            await evaluator.StartSessionAsync(req.SessionDate, req.SessionOpen);

            return Results.Ok(new StrategySessionResponse(
                evaluator.IsReady,
                evaluator.Predictions,
                evaluator.DailyAtr));
        });

        // Called on every bar close. Returns the signal the strategy should act on.
        app.MapPost(EVAL_BAR, (
            StrategyBarRequest req,
            StrategyEvaluator evaluator) =>
        {
            var position = req.Position switch
            {
                "Long"  => PositionSide.Long,
                "Short" => PositionSide.Short,
                _       => PositionSide.Flat,
            };

            var eval = evaluator.Evaluate(req.High, req.Low, req.Close, position, req.SessionTime, req.EntryPrice);
            return Results.Ok(new StrategyBarResponse(eval.Signal.ToString(), eval.StopPrice));
        });
    }
}
