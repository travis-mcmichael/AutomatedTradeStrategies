CREATE TABLE IF NOT EXISTS BarData (
    -- Identity
    Id INTEGER PRIMARY KEY AUTOINCREMENT,

    -- Instrument & timeframe
    Instrument TEXT NOT NULL,
    Timeframe TEXT NOT NULL,              -- e.g. '1m', '5m', '1s'
    
    -- Timestamp
    TimestampUtc INTEGER NOT NULL,        -- Unix epoch milliseconds
    SessionDate TEXT,                     -- YYYY-MM-DD (optional partition helper)

    -- OHLCV
    Open REAL NOT NULL,
    High REAL NOT NULL,
    Low REAL NOT NULL,
    Close REAL NOT NULL,
    Volume REAL NOT NULL,
);
