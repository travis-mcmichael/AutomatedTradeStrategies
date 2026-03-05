using AutomatedTrading.Core.Models;
using Microsoft.Data.Sqlite;

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

var scalpEvaluator = new ScalpStrategyEvaluator();
var protoEvaluator20260226 = new ProtoPredictiveEvaluator();

app.MapPost("/loadModels", () =>
{
    protoEvaluator20260226.LoadModel();
    return Results.Ok("Models Loaded");
});

app.MapPost("/end", () =>
{
    protoEvaluator20260226.Dispose();
    return Results.Ok("Session(s) disposed.");
});

app.MapPost("/sessionLoaded", () =>
{
    return Results.Ok(protoEvaluator20260226.SessionLoaded());
});

app.MapPost("/metaSessionLoaded", () =>
{
    return Results.Ok(scalpEvaluator.SessionLoaded("meta"));
});

app.MapPost("/scalerLoaded", () =>
{
    return Results.Ok(protoEvaluator20260226.ScalerLoaded);
});

app.MapPost("/computeFeatures20260226", () =>
{
    
});

app.MapPost("/computeFeatures", (ComputeFeaturesRequest req) =>
{
    var features = scalpEvaluator.ComputeFeatures(
        req.BarTime.Value,
        req.Atr14.Value,
        req.VolumeAccel.Value,
        req.SessionHigh.Value,
        req.SessionLow.Value,
        req.TickCountThisBar.Value,
        req.Bars ?? new List<Bar>());
    return Results.Ok(new FeaturesResponse(features));
});

app.MapPost("/scaleFeatures", (FeaturesRequest req) =>
{
    var features = protoEvaluator20260226.ScaleFeatures(req.Features);
    return Results.Ok(new FeaturesResponse(features));
});

app.MapPost("/runBaseInference", (FeaturesRequest req) =>
{
    var result = protoEvaluator20260226.RunInference(req.Features);
    return Results.Ok(new InferenceResponse(result));
});

app.MapPost("/predict", (AutomatedTrading.Core.Models.Requests.Request20260226.FeaturesRequest req) =>
{
    var scaled = protoEvaluator20260226.ScaleFeatures(req.Features);
    var prob = protoEvaluator20260226.RunInference(scaled);
    return Results.Ok(prob);
});

app.MapPost("/runMetaInference", (MetaInferenceRequest req) =>
{
   var result = scalpEvaluator.RunMetaInference(req.Scaled, req.BaseProbability.Value);
   return Results.Ok(new InferenceResponse(result)); 
});

app.MapPost("/cleanupTicks", async (IConfiguration config) =>
{
    var connectionString = config.GetConnectionString("DefaultConnection");

    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();

    // Compute cutoff for 3 months ago
    var cutoff = DateTimeOffset.UtcNow.AddMonths(-3).ToUnixTimeMilliseconds();

    await using var command = connection.CreateCommand();
    command.CommandText = "DELETE FROM TickData WHERE TickTime < $cutoff;";
    command.Parameters.AddWithValue("$cutoff", cutoff);

    var rowsDeleted = await command.ExecuteNonQueryAsync();

    return Results.Ok(new { deletedRows = rowsDeleted });
});

app.MapPost("/insertTicks", async (InsertTicksRequest request, IConfiguration config) =>
{
    if (request.Ticks.Count == 0)
        return Results.BadRequest("No ticks provided");

    await using var connection = new SqliteConnection("Data Source=C:\\dev\\SQLite\\home.db;");
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    var command = connection.CreateCommand();
    command.CommandText = @"
        INSERT OR REPLACE INTO TickData
        (Instrument, TickTime, LastPrice, Volume, Bid, Ask)
        VALUES ($instrument, $tickTime, $lastPrice, $volume, $bid, $ask);";
    
    var instrumentParam = command.CreateParameter();
    instrumentParam.ParameterName = "$instrument";
    command.Parameters.Add(instrumentParam);

    var tickTimeParam = command.CreateParameter();
    tickTimeParam.ParameterName = "$tickTime";
    command.Parameters.Add(tickTimeParam);

    var lastPriceParam = command.CreateParameter();
    lastPriceParam.ParameterName = "$lastPrice";
    command.Parameters.Add(lastPriceParam);

    var volumeParam = command.CreateParameter();
    volumeParam.ParameterName = "$volume";
    command.Parameters.Add(volumeParam);

    var bidParam = command.CreateParameter();
    bidParam.ParameterName = "$bid";
    command.Parameters.Add(bidParam);

    var askParam = command.CreateParameter();
    askParam.ParameterName = "$ask";
    command.Parameters.Add(askParam);

    foreach (var tick in request.Ticks)
    {
        instrumentParam.Value = tick.Instrument;
        tickTimeParam.Value = tick.TickTime;
        lastPriceParam.Value = tick.LastPrice;
        volumeParam.Value = tick.Volume;
        bidParam.Value = tick.Bid ?? (object)DBNull.Value;
        askParam.Value = tick.Ask ?? (object)DBNull.Value;

        await command.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();

    return Results.Ok(new { inserted = request.Ticks.Count });
});

app.Run();
