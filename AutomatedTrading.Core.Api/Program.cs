using AutomatedTrading.Core.Api.Endpoints;
using AutomatedTrading.Core.Api.Services;
using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.DataLayer.Sqlite;
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

// Strategy evaluator (singleton — holds per-session trading state)
builder.Services.AddSingleton(sp =>
    new PredictedHighStrategyEvaluator(
        sp.GetRequiredService<IBarHighPredictor>(),
        sp.GetRequiredService<IBarRepository>()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();

BarHighEndpoints.Map(app);
BarLowEndpoints.Map(app);
PredictedHighEndpoints.Map(app);
PredictedLowEndpoints.Map(app);

app.Run();
