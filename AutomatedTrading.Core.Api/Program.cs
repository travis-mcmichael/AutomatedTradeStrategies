using AutomatedTrading.Core.Api.Endpoints;
using AutomatedTrading.Core.Framework.Logging;
using AutomatedTrading.Core.Api.Models.Predict;
using AutomatedTrading.Core.Api.Models.Evaluate;
using AutomatedTrading.Core.Api.Services;
using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.DataLayer.Sqlite;

Logger.Configure();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Data access
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;
var connectionFactory = new SqliteConnectionFactory(connectionString);
builder.Services.AddSingleton<IDbConnectionFactory>(_ => connectionFactory);
builder.Services.AddSingleton<IBarRepository, BarRepository>();
builder.Services.AddSingleton<IModelConfigRepository, ModelConfigRepository>();

var models = await new ModelConfigRepository(connectionFactory).GetAllAsync();

foreach (var model in models.Where(m => m.Active && m.ModelFeatures.Count > 0))
{
    var captured = model;
    builder.Services.AddKeyedSingleton<IMovementPredictor>(
        captured.ModelName,
        (sp, _) => new InferenceService(captured));
}

// Unified strategy evaluator — collects all active predictors paired with their
// ModelEntry (for window configuration) into a dictionary.
builder.Services.AddSingleton(sp =>
{
    var predictors = models
        .Where(m => m.Active && m.ModelFeatures.Count > 0)
        .ToDictionary(
            m => m.ModelName,
            m => (sp.GetRequiredKeyedService<IMovementPredictor>(m.ModelName), m));
    return new StrategyEvaluator(predictors, sp.GetRequiredService<IBarRepository>());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();

StrategyEndpoints.Map(app);
ModelEndpoints.Map(app);

app.Run();
