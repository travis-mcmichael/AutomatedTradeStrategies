using System;
using Microsoft.Extensions.Configuration;

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
    public static readonly string XGB_MODEL_FILENAME = "xgb_model.onnx";
    public static readonly string SCALER_FILENAME = "scaler_params.json";

}
