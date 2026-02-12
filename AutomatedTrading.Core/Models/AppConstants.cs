namespace AutomatedTrading.Core.Models;

public class AppConstants
{
    /// <summary>
    /// Strategies are meant to trade on MES MAR2026 Instrument @ $5 per point
    /// </summary>
    public const double DOLLARS_PER_POINT = 5.0;

    /// <summary>
    /// Time to start the trade day.
    /// </summary>
    public const int TRADE_DAY_START = 80000;

    /// <summary>
    ///  Time to end the trade day.
    /// </summary>
    public const int TRADE_DAY_END = 160000;

    /// <summary>
    /// Minimum # of Bars required to perform moderate complexity logical operations on day's trading data.
    /// </summary>
    public const int MINIMUM_BARS_TO_EVALUATE = 30;

    /// <summary>
    /// Directory where all the test data files are stored.
    /// </summary>
    public const string TEST_BASE_DIRECTORY = "TestData";
}