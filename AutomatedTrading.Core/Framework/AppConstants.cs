using System;
using Microsoft.Extensions.Configuration;

namespace AutomatedTrading.Core.Framework;

public static class AppConstants
{
    private static readonly IConfigurationRoot Config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .Build();

    /// <summary>
    /// Strategies are meant to trade on MES MAR2026 Instrument @ $5 per point
    /// </summary>
    public static readonly double DOLLARS_PER_POINT = Config.GetValue<double>("AppSettings:DollarsPerPoint");

    /// <summary>
    /// Time to start the trade day.
    /// </summary>
    public static readonly int TRADE_DAY_START = Config.GetValue<int>("AppSettings:TradeDayStart");

    /// <summary>
    /// Time to end the trade day.
    /// </summary>
    public static readonly int TRADE_DAY_END = Config.GetValue<int>("AppSettings:TradeDayEnd");

    /// <summary>
    /// Minimum # of Bars required to perform moderate complexity logical operations on day's trading data.
    /// </summary>
    public static readonly int MINIMUM_BARS_TO_EVALUATE = Config.GetValue<int>("AppSettings:MinimumBarsToEvaluate");

    /// <summary>
    /// Directory where all the test data files are stored.
    /// </summary>
    public static readonly string TEST_BASE_DIRECTORY = Config.GetValue<string>("AppSettings:TestBaseDirectory")!;

    /// <summary>
    /// Test data was exported in UTC. This is to correct that (easier than editing exported data).
    /// </summary>
    public static readonly int TEST_DATA_OFFSET_HOURS = Config.GetValue<int>("AppSettings:TestDataOffsetHours");

    /// <summary>
    /// Default connection for the SQLite database.
    /// </summary>
    public static readonly string SQLITE_CONNECTION = Config.GetValue<string>("AppSettings:SqliteConnection")!;

    public static readonly int TICK_DATA_BATCH_SIZE = Config.GetValue<int>("AppSettings:TickDataBatchSize");

    public static readonly int BAR_DATA_BATCH_SIZE = Config.GetValue<int>("AppSettings:BarDataBatchSize");
}
