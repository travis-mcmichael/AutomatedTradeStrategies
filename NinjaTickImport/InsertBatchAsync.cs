using AutomatedTrading.Core.Models;
using Microsoft.Data.Sqlite;
namespace NinjaTickDataImport;
public static class InsertBatchAsync
{
    public static async Task<int> Insert(SqliteConnection connection, List<TickData> batch)
    {
        using var transaction = await connection.BeginTransactionAsync();
        var command = connection.CreateCommand();
        command.CommandText = @"
        INSERT OR REPLACE INTO TickData
        (Instrument, TickTime, LastPrice, Volume, Bid, Ask)
        VALUES ($instrument, $tickTime, $lastPrice, $volume, $bid, $ask);";
        var instrumentParam = command.CreateParameter(); instrumentParam.ParameterName = "$instrument"; command.Parameters.Add(instrumentParam);
        var tickTimeParam = command.CreateParameter(); tickTimeParam.ParameterName = "$tickTime"; command.Parameters.Add(tickTimeParam);
        var lastPriceParam = command.CreateParameter(); lastPriceParam.ParameterName = "$lastPrice"; command.Parameters.Add(lastPriceParam);
        var volumeParam = command.CreateParameter(); volumeParam.ParameterName = "$volume"; command.Parameters.Add(volumeParam);
        var bidParam = command.CreateParameter(); bidParam.ParameterName = "$bid"; command.Parameters.Add(bidParam);
        var askParam = command.CreateParameter(); askParam.ParameterName = "$ask"; command.Parameters.Add(askParam);
        
        foreach (var tick in batch)
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
        return batch.Count;
    }
}