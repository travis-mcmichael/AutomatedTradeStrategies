using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Utility;

namespace AutomatedTrading.Core.Data.Queries;

// Bars for one instrument across an inclusive range of session dates (yyyyMMdd), in time order. Non-trading sessions
// are dropped.
public class GetBarsInSessionRange(IDbConnectionFactory connectionFactory)
{
    public async Task<List<BarData>> Execute(string instrument, string from, string to)
    {
        using var connection = connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT Timeframe, Timestamp, SessionDate, SessionTime, Open, High, Low, Close, Volume, Instrument, InstrumentVersion
              FROM BarData
             WHERE Instrument = @Instrument
               AND SessionDate BETWEEN @From AND @To
             ORDER BY Timestamp";

        SqlHelper.AddParameter(cmd, "@Instrument", instrument);
        SqlHelper.AddParameter(cmd, "@From", from);
        SqlHelper.AddParameter(cmd, "@To", to);

        await connection.OpenAsync();

        var bars = new List<BarData>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var bar = new BarData
            {
                Timeframe = reader.GetString(0),
                Timestamp = MarketTime.FromUnixMilliseconds(reader.GetInt64(1)),
                SessionDate = reader.IsDBNull(2) ? null : reader.GetString(2),
                SessionTime = reader.IsDBNull(3) ? null : reader.GetString(3),
                Open = reader.GetDouble(4),
                High = reader.GetDouble(5),
                Low = reader.GetDouble(6),
                Close = reader.GetDouble(7),
                Volume = reader.GetDouble(8),
                Instrument = reader.GetString(9),
                InstrumentVersion = reader.GetString(10),
            };
            if (bar.SessionDate is not null && TradingCalendar.IsTradingDay(bar.SessionDate))
                bars.Add(bar);
        }
        return bars;
    }
}
