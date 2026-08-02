using System;

namespace AutomatedTrading.Core.Framework;

public static class AppConstants
{
    public static readonly double DOLLARS_PER_POINT = 5.0;
    public static readonly int TRADE_DAY_START = 83000;
    public static readonly int TRADE_DAY_END = 160000;
    public static readonly int MINIMUM_BARS_TO_EVALUATE = 15;
    public static readonly string TEST_BASE_DIRECTORY = "TestData";
    public static readonly int TEST_DATA_OFFSET_HOURS = -5;
    public static readonly string SQLITE_CONNECTION = "Data Source=C:\\dev\\data\\SQLite\\home.db";
    public static readonly int TICK_DATA_BATCH_SIZE = 250;
    public static readonly int BAR_DATA_BATCH_SIZE = 250;
    public static readonly string MODEL_DIR = "C:\\dev\\AutomatedTradeStrategies\\Models";
    public static readonly string DATE_FORMAT = "yyyyMMdd";
    public static readonly string TIME_FORMAT = "HHmmss";

    // MES session times in CT (timestamps assumed to be in CT)
    // Session opens 16:00 (5 PM ET), RTH 08:30-15:15 (9:30 AM - 4:15 PM ET)
    public static readonly TimeSpan RTH_START_TIME = new (8, 30, 0);
    public static readonly TimeSpan RTH_END_TIME = new (15, 15, 0);
    public static readonly TimeSpan SESSION_OPEN_TIME = new (16, 0, 0);
    public static readonly string XGB_MODEL_FILENAME = "xgb_model.onnx";
    public static readonly string SCALER_FILENAME = "scaler_params.json";
    public static readonly string MODELS_CONFIG_PATH = $"C:\\dev\\AutomatedTradeStrategies\\Models\\Config\\models.json";
}
