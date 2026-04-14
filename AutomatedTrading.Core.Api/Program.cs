using AutomatedTrading.Core.Api.Endpoints;
using AutomatedTrading.Core.Framework.Logging;
using AutomatedTrading.Core.Api.Models.API.Predict;
using AutomatedTrading.Core.Api.Models.Evaluate;
using AutomatedTrading.Core.Api.Services;
using AutomatedTrading.Core.Framework.Config;
using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.DataLayer.Sqlite;

Logger.Configure();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Data access
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;
builder.Services.AddSingleton<IDbConnectionFactory>(_ => new SqliteConnectionFactory(connectionString));
builder.Services.AddSingleton<IBarRepository, BarRepository>();

// ML inference — one keyed IMovementPredictor singleton per active model that has
// a feature list defined in Models/Config/models.json.
var config = ModelConfig.GetConfig()
    ?? throw new InvalidOperationException("Failed to load Models/Config/models.json.");

foreach (var model in config.Models.Where(m => m.Active && m.Features.Count > 0))
{
    var captured = model;
    builder.Services.AddKeyedSingleton<IMovementPredictor>(
        captured.ModelName,
        (sp, _) => new InferenceService(
            sp.GetRequiredService<IBarRepository>(),
            captured));
}

// Unified strategy evaluator — collects all active predictors into a dictionary.
builder.Services.AddSingleton(sp =>
{
    var predictors = config.Models
        .Where(m => m.Active && m.Features.Count > 0)
        .ToDictionary(
            m => m.ModelName,
            m => sp.GetRequiredKeyedService<IMovementPredictor>(m.ModelName));
    return new StrategyEvaluator(predictors, sp.GetRequiredService<IBarRepository>());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();

StrategyEndpoints.Map(app);

app.Run();
