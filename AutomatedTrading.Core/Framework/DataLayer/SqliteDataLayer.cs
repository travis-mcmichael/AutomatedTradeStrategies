using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutomatedTrading.Core.Models;
using Microsoft.Data.Sqlite;

namespace AutomatedTrading.Core.Framework.DataLayer;

public class SqliteDataLayer
{
    #region - Get Tick Data -

    private static string tickDataSelect = "SELECT TickTime, LastPrice, Bid, Ask, Volume FROM TickData ORDER BY TickTime ASC";
    public static async Task<SqliteDataReader> GetTickDataReader()
    {
        var connection = new SqliteConnection(AppConstants.SQLITE_CONNECTION);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = tickDataSelect;
        return await command.ExecuteReaderAsync(System.Data.CommandBehavior.CloseConnection);
    }

    #endregion

    #region - Get Bar Data - 

    private static string barDataSelect = @"
    SELECT
        Instrument,
        Timeframe,
        Timestamp,
        SessionDate,
        Open,
        High,
        Low,
        Close,
        Volume
    FROM BarData 
    WHERE Timestamp >= @StartTimestamp AND
        Timestamp <= @EndTimestamp
    ORDER BY Timestamp ASC";
    public static async Task<SqliteDataReader> GetBarDataReader(DateTime start, DateTime end)
    {
        var connection = new SqliteConnection(AppConstants.SQLITE_CONNECTION);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = barDataSelect;
        command.Parameters.AddWithValue($"@StartTimestamp", start);
        command.Parameters.AddWithValue($"@EndTimestamp", end);

        return await command.ExecuteReaderAsync(System.Data.CommandBehavior.CloseConnection);
    }

    private static string barDataSelect2 = @"
    SELECT
        Instrument,
        Timeframe,
        Timestamp,
        SessionDate,
        Open,
        High,
        Low,
        Close,
        Volume
    FROM BarData WHERE SessionDate = @SessionDate
    ORDER BY Timestamp";
    public static async Task<List<BarData>> GetBarsBySessionDate(string sessionDate)
    {
        var bars = new List<BarData>();
        using var connection = new SqliteConnection(AppConstants.SQLITE_CONNECTION);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = barDataSelect2;
        command.Parameters.AddWithValue($"@SessionDate", sessionDate);
        using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.CloseConnection);
        while (await reader.ReadAsync())
        {
            var bar = new BarData
            {
                Instrument = reader.GetString(0),
                Timeframe = reader.GetString(1),
                Timestamp = DateTime.Parse(reader.GetString(2)),
                SessionDate = reader.IsDBNull(3) ? null : reader.GetString(3),
                Open = reader.GetDouble(4),
                High = reader.GetDouble(5),
                Low = reader.GetDouble(6),
                Close = reader.GetDouble(7),
                Volume = reader.GetDouble(8)
            };
            bars.Add(bar);
        }
        return bars;
    }

    #endregion

    #region - Tick Data Insert -

    private static string tickDataInsert = @"
                INSERT OR REPLACE INTO TickData
                (Instrument, TickTime, LastPrice, Volume, Bid, Ask)
                VALUES ($instrument, $tickTime, $lastPrice, $volume, $bid, $ask);";
    public static async Task<int> InsertTickBatch(List<TickData> insert)
    {
        var total = 0;
        using var connection = new SqliteConnection(AppConstants.SQLITE_CONNECTION);
        await connection.OpenAsync();

        if (insert.Count > AppConstants.TICK_DATA_BATCH_SIZE)
        {
            for (var i = 0; i < insert.Count; i += AppConstants.TICK_DATA_BATCH_SIZE)
            {
                try
                {
                    IList<TickData> batch;
                if (insert.Skip(i).Count() > AppConstants.TICK_DATA_BATCH_SIZE)
                {
                    batch = insert.Skip(i).Take(AppConstants.TICK_DATA_BATCH_SIZE).ToList();
                }
                else
                {
                    batch = insert.Skip(i).ToList();
                }

                using var transaction = connection.BeginTransaction();
                var command = connection.CreateCommand();
                command.CommandText = tickDataInsert;

                var instrumentParam = command.CreateParameter(); instrumentParam.ParameterName = "$instrument"; command.Parameters.Add(instrumentParam);
                var tickTimeParam = command.CreateParameter(); tickTimeParam.ParameterName = "$tickTime"; command.Parameters.Add(tickTimeParam);
                var lastPriceParam = command.CreateParameter(); lastPriceParam.ParameterName = "$lastPrice"; command.Parameters.Add(lastPriceParam);
                var volumeParam = command.CreateParameter(); volumeParam.ParameterName = "$volume"; command.Parameters.Add(volumeParam);
                var bidParam = command.CreateParameter(); bidParam.ParameterName = "$bid"; command.Parameters.Add(bidParam);
                var askParam = command.CreateParameter(); askParam.ParameterName = "$ask"; command.Parameters.Add(askParam);

                for (var j = i; j < i + AppConstants.TICK_DATA_BATCH_SIZE; j++)
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

                transaction.Commit();
                total += batch.Count;
                }
                catch
                {
                    Console.WriteLine($"exception inserting record at batch i={i}");
                    throw;
                }
            }
        }
        else
        {
            using var transaction = connection.BeginTransaction();
            var command = connection.CreateCommand();
            command.CommandText = tickDataInsert;

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

            transaction.Commit();
            total += insert.Count;
        }

        return total;
    }

    #endregion

    #region - Bar Data Insert -
    private static string barDataInsert = @"
                INSERT OR REPLACE INTO BarData
                (
                    Instrument, Timeframe,
                    Timestamp, SessionDate,
                    Open, High, Low, Close, Volume
                )
                VALUES 
                (
                    $Instrument, $Timeframe,
                    $Timestamp, $SessionDate,
                    $Open, $High, $Low, $Close, $Volume
                );";
    public static async Task<int> InsertBarDataBatch(List<BarData> insert)
    {
        var total = 0;
        using var connection = new SqliteConnection(AppConstants.SQLITE_CONNECTION);
        await connection.OpenAsync();

        if (insert.Count > AppConstants.BAR_DATA_BATCH_SIZE)
        {
            for (var i = 0; i < insert.Count; i += AppConstants.BAR_DATA_BATCH_SIZE)
            {
                var rec = -1;
                try
                {
                    IList<BarData> batch;
					if (insert.Skip(i).Count() > AppConstants.BAR_DATA_BATCH_SIZE)
					{
						batch = insert.Skip(i).Take(AppConstants.BAR_DATA_BATCH_SIZE).ToList();
					}
					else
					{
						batch = insert.Skip(i).ToList();
					}
	
					using var transaction = connection.BeginTransaction();
					var command = connection.CreateCommand();
					command.CommandText = barDataInsert;
	
					var instrumentParam = command.CreateParameter(); instrumentParam.ParameterName = "$Instrument"; command.Parameters.Add(instrumentParam);
					var timeframeParam = command.CreateParameter(); timeframeParam.ParameterName = "$Timeframe"; command.Parameters.Add(timeframeParam);
					var timestampParam = command.CreateParameter(); timestampParam.ParameterName = "$Timestamp"; command.Parameters.Add(timestampParam);
					var sessionDateParam = command.CreateParameter(); sessionDateParam.ParameterName = "$SessionDate"; command.Parameters.Add(sessionDateParam);
					var openParam = command.CreateParameter(); openParam.ParameterName = "$Open"; command.Parameters.Add(openParam);
					var closeParam = command.CreateParameter(); closeParam.ParameterName = "$Close"; command.Parameters.Add(closeParam);
					var highParam = command.CreateParameter(); highParam.ParameterName = "$High"; command.Parameters.Add(highParam);
					var lowParam = command.CreateParameter(); lowParam.ParameterName = "$Low"; command.Parameters.Add(lowParam);
					var volumeParam = command.CreateParameter(); volumeParam.ParameterName = "$Volume"; command.Parameters.Add(volumeParam);

                    Console.WriteLine($"next batch: {i + AppConstants.BAR_DATA_BATCH_SIZE}, next batch count: {i + batch.Count - 1}, insert count: {insert.Count}");
                    var range = Math.Min(i + AppConstants.BAR_DATA_BATCH_SIZE, i + batch.Count);
					for (var j = i; j < range; j++)
					{
                        rec = j;
	
						var bar = insert[j];
	
						instrumentParam.Value = bar.Instrument;
						timeframeParam.Value = bar.Timeframe;
						timestampParam.Value = bar.Timestamp;
						sessionDateParam.Value = bar.SessionDate;
						openParam.Value = bar.Open;
						closeParam.Value = bar.Close;
						highParam.Value = bar.High;
						lowParam.Value = bar.Low;
						volumeParam.Value = bar.Volume;
                        
                        foreach (SqliteParameter p in command.Parameters)
                        {
                            if (p.Value is double d)
                            {
                                p.Value = safeValue((double?)p.Value);
                            }
                        }
	
						await command.ExecuteNonQueryAsync();
					}
	
					transaction.Commit();
					total += batch.Count;   
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error inserting BarData batch i={i},j={rec}");
                    throw ex;
                }
            }
        }
        else
        {
            using var transaction = connection.BeginTransaction();
            var command = connection.CreateCommand();
            command.CommandText = barDataInsert;

            var instrumentParam = command.CreateParameter(); instrumentParam.ParameterName = "$Instrument"; command.Parameters.Add(instrumentParam);
            var timeframeParam = command.CreateParameter(); timeframeParam.ParameterName = "$Timeframe"; command.Parameters.Add(timeframeParam);
            var timestampParam = command.CreateParameter(); timestampParam.ParameterName = "$Timestamp"; command.Parameters.Add(timestampParam);
            var sessionDateParam = command.CreateParameter(); sessionDateParam.ParameterName = "$SessionDate"; command.Parameters.Add(sessionDateParam);
            var openParam = command.CreateParameter(); openParam.ParameterName = "$Open"; command.Parameters.Add(openParam);
            var closeParam = command.CreateParameter(); closeParam.ParameterName = "$Close"; command.Parameters.Add(closeParam);
            var highParam = command.CreateParameter(); highParam.ParameterName = "$High"; command.Parameters.Add(highParam);
            var lowParam = command.CreateParameter(); lowParam.ParameterName = "$Low"; command.Parameters.Add(lowParam);
            var volumeParam = command.CreateParameter(); volumeParam.ParameterName = "$Volume"; command.Parameters.Add(volumeParam);

            for (var i = 0; i < insert.Count; i++)
            {
                var bar = insert[i];
                instrumentParam.Value = bar.Instrument;
                timeframeParam.Value = bar.Timeframe;
                timestampParam.Value = bar.Timestamp;
                sessionDateParam.Value = bar.SessionDate;
                openParam.Value = bar.Open;
                closeParam.Value = bar.Close;
                highParam.Value = bar.High;
                lowParam.Value = bar.Low;
                volumeParam.Value = bar.Volume;
                
                foreach (SqliteParameter p in command.Parameters)
                {
                    if (p.Value is double d)
                    {
                        p.Value = safeValue((double?)p.Value);
                    }
                }

                await command.ExecuteNonQueryAsync();
            }

            transaction.Commit();
            total += insert.Count;
        }

        return total;
    }

    private static object safeValue(double? v)
    {
        if (!v.HasValue || double.IsNaN(v.Value) || double.IsInfinity(v.Value))
            return DBNull.Value;
        return v;
    }

    #endregion
}