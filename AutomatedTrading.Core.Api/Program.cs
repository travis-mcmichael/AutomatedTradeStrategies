using AutomatedTrading.Core.Api.Endpoints;
using AutomatedTrading.Core.Framework.Logging;
using AutomatedTrading.Core.Api.Models.Predict;
using AutomatedTrading.Core.Api.Models.Evaluate;
using AutomatedTrading.Core.Api.Services;
using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Framework.DataLayer.Sqlite;
using AutomatedTrading.Core.Data;
using AutomatedTrading.Core.Data.Queries;
using AutomatedTrading.Core.Models;

Logger.Configure();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Data access
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;
var connectionFactory = new SqliteConnectionFactory(connectionString);
builder.Services.AddSingleton<IDbConnectionFactory>(_ => connectionFactory);
builder.Services.AddScoped(typeof(IDefaultRepository<>), typeof(DefaultRepository<>));
builder.Services.AddScoped(typeof(IDefaultInsertCommand<>), typeof(DefaultInsertCommand<>));
builder.Services.AddScoped(typeof(IDefaultUpdateCommand<>), typeof(DefaultUpdateCommand<>));
builder.Services.AddScoped(typeof(IDefaultDeleteCommand<>), typeof(DefaultDeleteCommand<>));

// Auto-register every query/command class so new ones don't need to be listed here.
var queryAndCommandTypes = typeof(IDatabaseEntity).Assembly.GetTypes()
    .Where(t => t is
    {
        IsClass: true,
        IsAbstract: false,
        Namespace: "AutomatedTrading.Core.Data.Queries" or "AutomatedTrading.Core.Data.Commands"
    });

foreach (var type in queryAndCommandTypes)
    builder.Services.AddScoped(type);

var models = await new GetAllModelConfigs(
    new DefaultRepository<ModelConfig>(connectionFactory, null),
    new DefaultRepository<ModelFeature>(connectionFactory, null),
    new DefaultRepository<ModelFeatureParam>(connectionFactory, null),
    new DefaultRepository<FeatureType>(connectionFactory, null),
    new DefaultRepository<FeatureTypeParam>(connectionFactory, null)
).Execute();

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
    return new StrategyEvaluator(predictors, sp.GetRequiredService<GetBarsBySessionDate>());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();

StrategyEndpoints.Map(app);
ModelEndpoints.Map(app);

app.Run();
