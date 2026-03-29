using AutomatedTrading.Core.Api.Endpoints;
using AutomatedTrading.Core.Api.Services;
using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.DataLayer.Sqlite;
using AutomatedTrading.Core.Framework.Predictors;
using AutomatedTrading.Core.Strategies;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Data access
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;
builder.Services.AddSingleton<IDbConnectionFactory>(_ => new SqliteConnectionFactory(connectionString));
builder.Services.AddSingleton<IBarRepository, BarRepository>();

// ML inference (singleton — holds loaded ONNX InferenceSessions)
builder.Services.AddSingleton<BarInferenceService>();
builder.Services.AddSingleton<IBarHighPredictor>(sp => sp.GetRequiredService<BarInferenceService>());
builder.Services.AddSingleton<IBarLowPredictor>(sp => sp.GetRequiredService<BarInferenceService>());
builder.Services.AddSingleton<ITradeDayHighPredictor>(sp => sp.GetRequiredService<BarInferenceService>());
builder.Services.AddSingleton<ITradeDayLowPredictor>(sp => sp.GetRequiredService<BarInferenceService>());

// Strategy evaluators (singleton — holds per-session trading state)
builder.Services.AddSingleton(sp =>
    new SessionHighStrategyEvaluator(
        sp.GetRequiredService<IBarHighPredictor>(),
        sp.GetRequiredService<IBarRepository>()));
builder.Services.AddSingleton(sp =>
    new SessionLowStrategyEvaluator(
        sp.GetRequiredService<IBarLowPredictor>(),
        sp.GetRequiredService<IBarRepository>()));
builder.Services.AddSingleton(sp =>
    new TradeDayHighStrategyEvaluator(
        sp.GetRequiredService<ITradeDayHighPredictor>(),
        sp.GetRequiredService<IBarRepository>()));
builder.Services.AddSingleton(sp =>
    new TradeDayLowStrategyEvaluator(
        sp.GetRequiredService<ITradeDayLowPredictor>(),
        sp.GetRequiredService<IBarRepository>()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();

SessionHighEndpoints.Map(app);
PredictedLowEndpoints.Map(app);
TradeDayHighEndpoints.Map(app);
TradeDayLowEndpoints.Map(app);

app.Run();
