using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutomatedTrading.Core.Models;
using Microsoft.Data.Sqlite;

namespace AutomatedTrading.Core.Framework;

public class SqliteDataLayer
{

    #region - Tick Data Insert -
    private static int tickBatchSize = 250;
    private static string TICK_DATA_INSERT = @"
                INSERT OR REPLACE INTO TickData
                (Instrument, TickTime, LastPrice, Volume, Bid, Ask)
                VALUES ($instrument, $tickTime, $lastPrice, $volume, $bid, $ask);";
    public static async Task<int> InsertTickBatch(List<TickData> insert)
    {
        var total = 0;
        using var connection = new SqliteConnection(AppConstants.SQLITE_CONNECTION);
        await connection.OpenAsync();

        if (insert.Count > tickBatchSize)
        {
            for (var i = 0; i < insert.Count; i += tickBatchSize)
            {
                IList<TickData> batch;
                if (insert.Skip(i).Count() > tickBatchSize)
                {
                    batch = insert.Skip(i).Take(tickBatchSize).ToList();
                }
                else
                {
                    batch = insert.Skip(i).ToList();
                }

                using var transaction = connection.BeginTransaction();
                var command = connection.CreateCommand();
                command.CommandText = TICK_DATA_INSERT;

                var instrumentParam = command.CreateParameter(); instrumentParam.ParameterName = "$instrument"; command.Parameters.Add(instrumentParam);
                var tickTimeParam = command.CreateParameter(); tickTimeParam.ParameterName = "$tickTime"; command.Parameters.Add(tickTimeParam);
                var lastPriceParam = command.CreateParameter(); lastPriceParam.ParameterName = "$lastPrice"; command.Parameters.Add(lastPriceParam);
                var volumeParam = command.CreateParameter(); volumeParam.ParameterName = "$volume"; command.Parameters.Add(volumeParam);
                var bidParam = command.CreateParameter(); bidParam.ParameterName = "$bid"; command.Parameters.Add(bidParam);
                var askParam = command.CreateParameter(); askParam.ParameterName = "$ask"; command.Parameters.Add(askParam);

                for (var j = i; j < i + tickBatchSize; j++)
                {

                    var tick = insert[j];
                    instrumentParam.Value = tick.Instrument;
                    tickTimeParam.Value = tick.TickTime;
                    lastPriceParam.Value = tick.LastPrice;
                    volumeParam.Value = tick.Volume;
                    bidParam.Value = tick.Bid ?? (object)DBNull.Value;
                    askParam.Value = tick.Ask ?? (object)DBNull.Value;
                    await command.ExecuteNonQueryAsync();
                }
                
                await transaction.CommitAsync();
                total += batch.Count;
            }
        }
        else
        {
            using var transaction = connection.BeginTransaction();
            var command = connection.CreateCommand();
            command.CommandText = TICK_DATA_INSERT;
            var instrumentParam = command.CreateParameter(); instrumentParam.ParameterName = "$instrument"; command.Parameters.Add(instrumentParam);
            var tickTimeParam = command.CreateParameter(); tickTimeParam.ParameterName = "$tickTime"; command.Parameters.Add(tickTimeParam);
            var lastPriceParam = command.CreateParameter(); lastPriceParam.ParameterName = "$lastPrice"; command.Parameters.Add(lastPriceParam);
            var volumeParam = command.CreateParameter(); volumeParam.ParameterName = "$volume"; command.Parameters.Add(volumeParam);
            var bidParam = command.CreateParameter(); bidParam.ParameterName = "$bid"; command.Parameters.Add(bidParam);
            var askParam = command.CreateParameter(); askParam.ParameterName = "$ask"; command.Parameters.Add(askParam);

            for (var i = 0; i < insert.Count; i++)
            {
                var tick = insert[i];
                instrumentParam.Value = tick.Instrument;
                tickTimeParam.Value = tick.TickTime;
                lastPriceParam.Value = tick.LastPrice;
                volumeParam.Value = tick.Volume;
                bidParam.Value = tick.Bid ?? (object)DBNull.Value;
                askParam.Value = tick.Ask ?? (object)DBNull.Value;
                await command.ExecuteNonQueryAsync();
            }
                
            await transaction.CommitAsync();
            total += insert.Count;
        }

        return total;
    }

    #endregion

    #region - Bar Data Insert 

    private static int barBatchSize = 250;
    // todo; something about timestamp utc / time
    private static string BAR_DATA_INSERT = @"
                INSERT OR REPLACE INTO BarData
                (
                    Instrument, Timeframe,
                    TimestampUtc, SessionDate,
                    Open, High, Low, Close, Volume,
                    BidVolume, AskVolume, Delta, CumulativeDelta, VWAP,
                    ATR_14, TrueRange, RealizedVol_20,
                    RSI_14, RSI_7, MACD, MACDSignal, MACDHist, Momentum_10,
                    EMA_9, EMA_21, EMASpread, ADX_14,
                    BB_Upper, BB_Lower, BB_Width, PercentB, 
                    HighLowRange, ClosePosition,BodySize, UpperWick, LowerWick,
                    Return_1, Return_3, Return_5, Return_10,
                    MinutesFromOpen, IsOpening, IsMidDay, IsClosing,
                    FutureReturn_1, FutureReturn_3, FutureReturn_5, FutureReturn_10,
                    Label_Up_1, Label_Up_3, Label_Up_5, Label_Up_10
                )
                VALUES 
                (
                    $Instrument, $Timeframe,
                    $TimestampUtc, $SessionDate,
                    $Open, $High, $Low, $Close, $Volume,
                    $BidVolume, $AskVolume, $Delta, $CumulativeDelta, $VWAP,
                    $ATR_14, $TrueRange, $RealizedVol_20,
                    $RSI_14, $RSI_7, $MACD, $MACDSignal, $MACDHist, $Momentum_10,
                    $EMA_9, $EMA_21, $EMASpread, $ADX_14,
                    $BB_Upper, $BB_Lower, $BB_Width, $PercentB, $
                    $HighLowRange, $ClosePosition,BodySize, $UpperWick, $LowerWick,
                    $Return_1, $Return_3, $Return_5, $Return_10,
                    $MinutesFromOpen, $IsOpening, $IsMidDay, $IsClosing,
                    $FutureReturn_1, $FutureReturn_3, $FutureReturn_5, $FutureReturn_10,
                    $Label_Up_1, $Label_Up_3, $Label_Up_5, $Label_Up_10
                );";
    public static async Task<int> InsertBarDataBatch(List<BarData> insert)
    {
        var total = 0;
        using var connection = new SqliteConnection(AppConstants.SQLITE_CONNECTION);
        await connection.OpenAsync();

        if (insert.Count > tickBatchSize)
        {
            for (var i = 0; i < insert.Count; i += tickBatchSize)
            {
                IList<BarData> batch;
                if (insert.Skip(i).Count() > tickBatchSize)
                {
                    batch = insert.Skip(i).Take(tickBatchSize).ToList();
                }
                else
                {
                    batch = insert.Skip(i).ToList();
                }

                using var transaction = connection.BeginTransaction();
                var command = connection.CreateCommand();
                command.CommandText = BAR_DATA_INSERT;


                for (var j = i; j < i + tickBatchSize; j++)
                {

                    var tick = insert[j];

                    await command.ExecuteNonQueryAsync();
                }
                
                await transaction.CommitAsync();
                total += batch.Count;
            }
        }
        else
        {
            using var transaction = connection.BeginTransaction();
            var command = connection.CreateCommand();
            command.CommandText = BAR_DATA_INSERT;


            for (var i = 0; i < insert.Count; i++)
            {
                var tick = insert[i];

                await command.ExecuteNonQueryAsync();
            }
                
            await transaction.CommitAsync();
            total += insert.Count;
        }

        return total;
    }

    #endregion
}