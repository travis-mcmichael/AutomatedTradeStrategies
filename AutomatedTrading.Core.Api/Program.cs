using AutomatedTrading.Core.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

var evaluator = new ScalpStrategyEvaluator();

app.MapPost("/loadModels", () =>
{
    evaluator.LoadModels();
    return Results.Ok("Models Loaded");
});

app.MapPost("/end", () =>
{
    evaluator.Dispose();
    return Results.Ok("Session(s) disposed.");
});

app.MapPost("/sessionLoaded", () =>
{
    return Results.Ok(evaluator.SessionLoaded("base"));
});

app.MapPost("/metaSessionLoaded", () =>
{
    return Results.Ok(evaluator.SessionLoaded("meta"));
});

app.MapPost("/scalerLoaded", () =>
{
    return Results.Ok(evaluator.ScalerLoaded);
});

app.MapPost("/computeFeatures", (ComputeFeaturesRequest req) =>
{
    var features = evaluator.ComputeFeatures(req.BarTime.Value, req.Atr14.Value, req.VolumeAccel.Value, req.SessionHigh.Value, req.SessionLow.Value, req.TickCountThisBar.Value, req.Bars ?? new List<Bar>());
    return Results.Ok(new FeaturesResponse(features));
});

app.MapPost("/scaleFeatures", (FeaturesRequest req) =>
{
    var features = evaluator.ScaleFeatures(req.Features);
    return Results.Ok(new FeaturesResponse(features));
});

app.MapPost("/runBaseInference", (FeaturesRequest req) =>
{
    var result = evaluator.RunBaseInterference(req.Features);
    return Results.Ok(new InferenceResponse(result));
});

app.MapPost("/runMetaInference", (MetaInferenceRequest req) =>
{
   var result = evaluator.RunMetaInference(req.Scaled, req.BaseProbability.Value);
   return Results.Ok(new InferenceResponse(result)); 
});

app.Run();
