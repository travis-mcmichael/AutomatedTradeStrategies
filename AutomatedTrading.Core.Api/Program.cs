using AutomatedTrading.Core.Api.Endpoints;
using AutomatedTrading.Core.Api.Services;
using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.DataLayer.Sqlite;
using AutomatedTrading.Core.Models;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Data access
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;
builder.Services.AddSingleton<IDbConnectionFactory>(_ => new SqliteConnectionFactory(connectionString));
builder.Services.AddSingleton<IBarRepository, BarRepository>();

// ML inference (singleton — holds loaded ONNX InferenceSessions)
builder.Services.AddSingleton<BarInferenceService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();

// ──────────────────────────────────────────────────────────────────────────────
// Inference endpoints
// ──────────────────────────────────────────────────────────────────────────────

BarHighEndpoints.Map(app);
BarLowEndpoints.Map(app);

// ──────────────────────────────────────────────────────────────────────────────
// Tick data endpoints
// ──────────────────────────────────────────────────────────────────────────────

app.MapPost("/cleanupTicks", async (IConfiguration config) =>
{
    var cs = config.GetConnectionString("DefaultConnection");
    await using var connection = new SqliteConnection(cs);
    await connection.OpenAsync();

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

    var cs = config.GetConnectionString("DefaultConnection");
    await using var connection = new SqliteConnection(cs);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    var command = connection.CreateCommand();
    command.CommandText = @"
        INSERT OR REPLACE INTO TickData
        (Instrument, TickTime, LastPrice, Volume, Bid, Ask)
        VALUES ($instrument, $tickTime, $lastPrice, $volume, $bid, $ask);";

    var instrumentParam = command.CreateParameter(); instrumentParam.ParameterName = "$instrument"; command.Parameters.Add(instrumentParam);
    var tickTimeParam   = command.CreateParameter(); tickTimeParam.ParameterName   = "$tickTime";   command.Parameters.Add(tickTimeParam);
    var lastPriceParam  = command.CreateParameter(); lastPriceParam.ParameterName  = "$lastPrice";  command.Parameters.Add(lastPriceParam);
    var volumeParam     = command.CreateParameter(); volumeParam.ParameterName     = "$volume";     command.Parameters.Add(volumeParam);
    var bidParam        = command.CreateParameter(); bidParam.ParameterName        = "$bid";        command.Parameters.Add(bidParam);
    var askParam        = command.CreateParameter(); askParam.ParameterName        = "$ask";        command.Parameters.Add(askParam);

    foreach (var tick in request.Ticks)
    {
        instrumentParam.Value = tick.Instrument;
        tickTimeParam.Value   = tick.TickTime;
        lastPriceParam.Value  = tick.LastPrice;
        volumeParam.Value     = tick.Volume;
        bidParam.Value        = tick.Bid ?? (object)DBNull.Value;
        askParam.Value        = tick.Ask ?? (object)DBNull.Value;
        await command.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();
    return Results.Ok(new { inserted = request.Ticks.Count });
});

app.Run();
