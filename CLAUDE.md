# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Build entire solution
dotnet build AutomatedTradingStrategies.slnx --configuration Release

# Build a specific project
dotnet build AutomatedTrading.Core/AutomatedTrading.Core.csproj

# Run the API
dotnet run --project AutomatedTrading.Core.Api

# Run the Console (backtesting / inference accuracy — mode selected in Program.cs)
dotnet run --project AutomatedTrading.Console
```

`AutomatedTrading.Core` multi-targets `net48` and `net10.0`; all other projects target `net10.0` only. The test project targets `net8.0`.

## Tests

The test project (`AutomatedTrading.Core.Test`) is **commented out of the solution file** and must be built/run directly.

```bash
# Run all tests
dotnet test AutomatedTrading.Core.Test/AutomatedTrading.Core.Test.csproj

# Run a single test class
dotnet test AutomatedTrading.Core.Test/AutomatedTrading.Core.Test.csproj \
  --filter "FullyQualifiedName~IsTrendingSignalTest"

# Run a single test method
dotnet test AutomatedTrading.Core.Test/AutomatedTrading.Core.Test.csproj \
  --filter "FullyQualifiedName~TrailingStopStrategyTest.UpdatesStopLoss_WhenStopUpdateTriggered"
```

Test data (`.dat` files exported from NinjaTrader) lives in `AutomatedTrading.Core.Test/TestData/` and is copied to the output directory automatically.

## Running the ML Pipeline

The full pipeline (feature export → train → ONNX export → copy models) is orchestrated by a shell script:

```bash
bash Shell/mlpipeline.sh
```

Individual steps:
```bash
# 1. Export features from SQLite to features.csv (per model)
dotnet run --project AutomatedTrading.Export.Feature --configuration Release

# 2. Train XGBoost and export ONNX (Python — activate venv first)
source /home/superuser/onnx-env/bin/activate
python Python/train_model_xgboost.py
python Python/export_to_onnx.py

# 3. Import NinjaTrader CSV exports into SQLite
dotnet run --project AutomatedTrading.Import -- <directory> bar   # or tick
```

Python writes intermediate model artifacts to `/mnt/c/dev/data/features/<modelName>/<date>/` (`xgb_model.onnx` + `scaler_params.json`). Step 4 of the pipeline copies these into `Models/<modelName>/<date>/` where the API can load them.

## Architecture Overview

This is a **machine-learning-driven MES (Micro E-mini S&P 500) futures trading platform**. The data flow is:

```
NinjaTrader (live) ──bar/tick data──► SQLite DB (home.db)
                                            │
                       AutomatedTrading.Export.Feature
                                            │ features.csv
                            Python training (XGBoost + ONNX export)
                                            │ xgb_model.onnx + scaler_params.json
                                    Models/<name>/<version>/
                                            │
                         AutomatedTrading.Core.Api (ASP.NET, port 5244)
                              │ POST /strategy/session
                              │ POST /strategy/bar
                    NinjaTrader Strategies (HTTP client)
```

### Projects

| Project | Purpose |
|---|---|
| `AutomatedTrading.Core` | Shared domain logic: signals, strategies, ML evaluator, data access, config |
| `AutomatedTrading.Core.Api` | ASP.NET Web API — serves ML inference to NinjaTrader strategies |
| `AutomatedTrading.Core.Test` | xUnit tests for signals and strategies |
| `AutomatedTrading.Console` | Backtesting and inference accuracy testing against the live API |
| `AutomatedTrading.Export.Feature` | Reads SQLite bars, computes features, writes `features.csv` per model |
| `AutomatedTrading.Import` | Imports NinjaTrader-exported CSV files (bar or tick) into SQLite |
| `NinjaTraderStrategies/` | NinjaScript files pasted into NinjaTrader — not part of the .NET solution |
| `Python/` | XGBoost training and ONNX export scripts |
| `SQL/` | SQLite DDL for `BarData` and `TickData` tables |

## Key Abstractions

**`ISignal`** (`AutomatedTrading.Core/Signals/`)  
Single method `bool IsTriggered(List<BarData> bars)`. Implementations: `IsTrending`, `IsPeakLocated`, `IsValleyLocated`, `IsSessionLow`, `InTradeDay`, `InTradeWindow`, `BarChange`.

**`IMovementPredictor`** (`AutomatedTrading.Core.Api/Models/Predict/`)  
Single method `Task<MovementPrediction?> PredictMovement(string sessionDate, double sessionOpen)`. Implemented by `InferenceService`. Registered as keyed DI singletons — one per active model in `models.json`.

**`StrategyEvaluator`** (`AutomatedTrading.Core.Api/Models/Evaluate/`)  
Holds all `IMovementPredictor` instances. Called at session open (runs all predictors, resolves working high/low price levels) and on each bar close (evaluates proximity to predicted levels, manages trailing stop). The active levels are resolved by `resolveActiveLevels`, which looks up predictions by **hardcoded model name keys** (`"mes.trade-day-bar.high"`, `"mes.session-day-bar.high"`, `"mes.trade-day-bar.low"`, `"mes.session-day-bar.low"`); trade-day takes precedence. Adding new model names that should drive signals requires updating this method. Evaluation also skips the first 15 bars of each session (`MINIMUM_BARS_TO_EVALUATE`).

**`BaseMLEvaluator`** (`AutomatedTrading.Core/Framework/ML/`)  
Wraps ONNX Runtime. Loads `.onnx` model and `scaler_params.json` (plain JSON, not pickle). Applies z-score scaling before inference. Handles regression (float tensor) and classification (ZipMap probability) outputs.

**`FeatureComputations` + `FeatureContext`** (`AutomatedTrading.Core/Framework/Config/`)  
Config-driven feature dispatch shared between training export and live inference — eliminates training/serving skew. A `ModelEntry` from `models.json` carries a list of named features; `ParseFeatures` dispatches each to its computation (ATR, RSI, SMA ratios, candlestick shape, overnight/pre-market stats, calendar features). Feature `source` values: `"daily"` (default, uses prev-session daily bar sequence), `"prevMinute"` (prev session's minute bars → `PrevBars`), `"currentMinute"` (current session's pre-market minute bars → `CurrentBars`). `InferenceService` treats a model as trade-day if **any** feature has `source: "currentMinute"`, and refuses to predict if those bars are absent.

**`IBarRepository` / `BarRepository`**  
Repository over SQLite. Key operations: `StreamBarsAsync` (streaming/low-memory), `GetBarsBySessionDateAsync`, `InsertBatchAsync`. Commands are separated into individual command classes under `Framework/DataLayer/Commands/`.

**`TrailingStopStrategy`** (`AutomatedTrading.Core/Strategies/`)  
Standalone trailing stop: activates after a configurable trigger distance from entry, then ratchets the stop by `TrailDistance` points as price moves favorably.

## API Endpoints

The API runs on `http://localhost:5244`. **The API must be running before NinjaTrader strategies are enabled.**

- **`POST /strategy/session`** — Called once at session open. Runs all active ML predictors, loads 20-day ATR. Returns `{ isReady, predictions, dailyAtr }` where `predictions` maps model names to absolute price levels.
- **`POST /strategy/bar`** — Called on every bar close. Accepts `{ high, low, close, position, entryPrice }`. Returns `{ signal, stopPrice }` where `signal` is one of: `None`, `EnterShort`, `EnterLong`, `ExitLong`, `ExitShort`, `UpdateStop`.

## Configuration

**`Models/Config/models.json`** — Central config for all ML models. Key fields per entry:
- `modelName` — key in API response dictionary; also used as subdirectory under `Models/` and the features base dir
- `target` — `"HIGH"`, `"LOW"`, or `"CLASS"`
- `active` — whether the API loads and runs this model
- `export` — whether the feature exporter and ML pipeline include this model
- `outputLocation` / `featureLocation` — base dirs for model artifacts and `features.csv` (used by Python pipeline)
- `version` — subdirectory under `Models/<modelName>/` where the API loads `.onnx` and `scaler_params.json`
- `features` — list of named features with optional `params` (integer lookup dict) and `source` (`"daily"` | `"prevMinute"` | `"currentMinute"`)
- `trainingStart` / `trainingEnd` — date range for feature export

**Hard-coded paths** (in `AppConstants.cs` and `appsettings.json`):
- SQLite DB: `C:\dev\data\SQLite\home.db`
- Model directory: `C:\dev\AutomatedTradeStrategies\Models`
- Log file: `/mnt/c/dev/logs/core.log` (log4net rolling daily)
- MES session times (CT): RTH 08:30–15:15, session open 16:00
- `DOLLARS_PER_POINT = 5.0`

## SQLite Schema

Two tables in `home.db` (DDL in `SQL/`):
- **`BarData`** — `Instrument`, `Timeframe`, `Timestamp` (Unix ms), `SessionDate` (yyyyMMdd), `SessionTime` (HHmmss), OHLCV. Unique on `(Instrument, Timestamp)`.
- **`TickData`** — `Instrument`, `TickTime` (Unix ms), `LastPrice`, `Volume`, `Bid`, `Ask`, `SessionDate`, `SessionTime`. Unique on `(Instrument, TickTime)`.

## NinjaTrader Integration

`NinjaTraderStrategies/` contains NinjaScript `.cs` files that are pasted directly into NinjaTrader — they are not compiled by the .NET solution. NinjaScript is synchronous, so API calls use `.GetAwaiter().GetResult()`.

- **`BarCollectionStrategy`** / **`TickCollectionStrategy`** — Write live market data into SQLite.
- **`PredictedHighStrategy`** / **`PredictedLowStrategy`** — Call `/strategy/session` at open and `/strategy/bar` on each RTH bar, then execute the returned signal.
