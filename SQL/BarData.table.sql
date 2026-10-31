CREATE TABLE IF NOT EXISTS BarData (
    -- Identity
    Id INTEGER PRIMARY KEY AUTOINCREMENT,

    -- Instrument & timeframe
    Instrument TEXT NOT NULL,
    InstrumentVersion TEXT NOT NULL,
    Timeframe TEXT NOT NULL,              -- e.g. '1m', '5m', '1s'
    
    -- Timestamp
    Timestamp INTEGER NOT NULL,         -- Unix epoch milliseconds
    SessionDate TEXT NOT NULL,          -- yyyyMMdd
    SessionTime TEXT NOT NULL,          -- HHmmss

    -- OHLCV
    Open REAL NOT NULL,
    High REAL NOT NULL,
    Low REAL NOT NULL,
    Close REAL NOT NULL,
    Volume REAL NOT NULL,

    UNIQUE (SessionDate, SessionTime, Instrument, InstrumentVersion)
);
