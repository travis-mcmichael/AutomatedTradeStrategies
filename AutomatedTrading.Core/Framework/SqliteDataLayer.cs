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

    // todo; something about timestamp utc / time
    private static string barDataInsert = @"
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

        if (insert.Count > AppConstants.BAR_DATA_BATCH_SIZE)
        {
            for (var i = 0; i < insert.Count; i += AppConstants.BAR_DATA_BATCH_SIZE)
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
                var timestampUtcParam = command.CreateParameter(); timestampUtcParam.ParameterName = "$TimestampUtc"; command.Parameters.Add(timestampUtcParam);
                var sessionDateParam = command.CreateParameter(); sessionDateParam.ParameterName = "$SessionDate"; command.Parameters.Add(sessionDateParam);
                var openParam = command.CreateParameter(); openParam.ParameterName = "$Open"; command.Parameters.Add(openParam);
                var closeParam = command.CreateParameter(); closeParam.ParameterName = "$Close"; command.Parameters.Add(closeParam);
                var highParam = command.CreateParameter(); highParam.ParameterName = "$High"; command.Parameters.Add(highParam);
                var lowParam = command.CreateParameter(); lowParam.ParameterName = "$Low"; command.Parameters.Add(lowParam);
                var volumeParam = command.CreateParameter(); volumeParam.ParameterName = "$Volume"; command.Parameters.Add(volumeParam);
                var bidVolumeParam = command.CreateParameter(); bidVolumeParam.ParameterName = "$BidVolume"; command.Parameters.Add(bidVolumeParam);
                var askVolumeParam = command.CreateParameter(); askVolumeParam.ParameterName = "$AskVolume"; command.Parameters.Add(askVolumeParam);
                var deltaParam = command.CreateParameter(); deltaParam.ParameterName = "$Delta"; command.Parameters.Add(deltaParam);
                var cumulativeDeltaParam = command.CreateParameter(); cumulativeDeltaParam.ParameterName = "$CumulativeDelta"; command.Parameters.Add(cumulativeDeltaParam);
                var vwapParam = command.CreateParameter(); vwapParam.ParameterName = "$VWAP"; command.Parameters.Add(vwapParam);
                var atr14Param = command.CreateParameter(); atr14Param.ParameterName = "$ATR_14"; command.Parameters.Add(atr14Param);
                var trueRangeParam = command.CreateParameter(); trueRangeParam.ParameterName = "$TrueRange"; command.Parameters.Add(trueRangeParam);
                var realizedVol20Param = command.CreateParameter(); realizedVol20Param.ParameterName = "$RealizedVol_20"; command.Parameters.Add(realizedVol20Param);
                var rsi14Param = command.CreateParameter(); rsi14Param.ParameterName = "$RSI_14"; command.Parameters.Add(rsi14Param);
                var rsi7Param = command.CreateParameter(); rsi7Param.ParameterName = "$RSI_7"; command.Parameters.Add(rsi7Param);
                var macdParam = command.CreateParameter(); macdParam.ParameterName = "$MACD"; command.Parameters.Add(macdParam);
                var macdSignalParam = command.CreateParameter(); macdSignalParam.ParameterName = "$MACDSignal"; command.Parameters.Add(macdSignalParam);
                var macdHistParam = command.CreateParameter(); macdHistParam.ParameterName = "$MACDHist"; command.Parameters.Add(macdHistParam);
                var momentum10Param = command.CreateParameter(); momentum10Param.ParameterName = "$Momentum_10"; command.Parameters.Add(momentum10Param);
                var ema9Param = command.CreateParameter(); ema9Param.ParameterName = "$EMA_9"; command.Parameters.Add(ema9Param);
                var ema21Param = command.CreateParameter(); ema21Param.ParameterName = "$EMA_21"; command.Parameters.Add(ema21Param);
                var emaSpreadParam = command.CreateParameter(); emaSpreadParam.ParameterName = "$EMASpread"; command.Parameters.Add(emaSpreadParam);
                var adx14Param = command.CreateParameter(); adx14Param.ParameterName = "$ADX_14"; command.Parameters.Add(adx14Param);
                var bbUpperParam = command.CreateParameter(); bbUpperParam.ParameterName = "$BB_Upper"; command.Parameters.Add(bbUpperParam);
                var bbLowerParam = command.CreateParameter(); bbLowerParam.ParameterName = "$BB_Lower"; command.Parameters.Add(bbLowerParam);
                var bbWidthParam = command.CreateParameter(); bbWidthParam.ParameterName = "$BB_Width"; command.Parameters.Add(bbWidthParam);
                var percentBParam = command.CreateParameter(); percentBParam.ParameterName = "$PercentB"; command.Parameters.Add(percentBParam);
                var highLowRangeParam = command.CreateParameter(); highLowRangeParam.ParameterName = "$HighLowRange"; command.Parameters.Add(highLowRangeParam);
                var closePositionParam = command.CreateParameter(); closePositionParam.ParameterName = "$ClosePosition"; command.Parameters.Add(closePositionParam);
                var bodySizeParam = command.CreateParameter(); bodySizeParam.ParameterName = "$BodySize"; command.Parameters.Add(bodySizeParam);
                var upperWickParam = command.CreateParameter(); upperWickParam.ParameterName = "$UpperWick"; command.Parameters.Add(upperWickParam);
                var lowerWickParam = command.CreateParameter(); lowerWickParam.ParameterName = "$LowerWick"; command.Parameters.Add(lowerWickParam);
                var return1Param = command.CreateParameter(); return1Param.ParameterName = "$Return_1"; command.Parameters.Add(return1Param);
                var return3Param = command.CreateParameter(); return3Param.ParameterName = "$Return_3"; command.Parameters.Add(return3Param);
                var return5Param = command.CreateParameter(); return5Param.ParameterName = "$Return_5"; command.Parameters.Add(return5Param);
                var return10Param = command.CreateParameter(); return10Param.ParameterName = "$Return_10"; command.Parameters.Add(return10Param);
                var minutesFromOpenParam = command.CreateParameter(); minutesFromOpenParam.ParameterName = "$MinutesFromOpen"; command.Parameters.Add(minutesFromOpenParam);
                var isOpeningParam = command.CreateParameter(); isOpeningParam.ParameterName = "$IsOpening"; command.Parameters.Add(isOpeningParam);
                var isMidDayParam = command.CreateParameter(); isMidDayParam.ParameterName = "$IsMidDay"; command.Parameters.Add(isMidDayParam);
                var isClosingParam = command.CreateParameter(); isClosingParam.ParameterName = "$IsClosing"; command.Parameters.Add(isClosingParam);
                var futureReturn1Param = command.CreateParameter(); futureReturn1Param.ParameterName = "$FutureReturn_1"; command.Parameters.Add(futureReturn1Param);
                var futureReturn3Param = command.CreateParameter(); futureReturn3Param.ParameterName = "$FutureReturn_3"; command.Parameters.Add(futureReturn3Param);
                var futureReturn5Param = command.CreateParameter(); futureReturn5Param.ParameterName = "$FutureReturn_5"; command.Parameters.Add(futureReturn5Param);
                var futureReturn10Param = command.CreateParameter(); futureReturn10Param.ParameterName = "$FutureReturn_10"; command.Parameters.Add(futureReturn10Param);
                var labelUp1Param = command.CreateParameter(); labelUp1Param.ParameterName = "$Label_Up_1"; command.Parameters.Add(labelUp1Param);
                var labelUp3Param = command.CreateParameter(); labelUp3Param.ParameterName = "$Label_Up_3"; command.Parameters.Add(labelUp3Param);
                var labelUp5Param = command.CreateParameter(); labelUp5Param.ParameterName = "$Label_Up_5"; command.Parameters.Add(labelUp5Param);
                var labelUp10Param = command.CreateParameter(); labelUp10Param.ParameterName = "$Label_Up_10"; command.Parameters.Add(labelUp10Param);

                for (var j = i; j < i + AppConstants.BAR_DATA_BATCH_SIZE; j++)
                {

                    var bar = insert[j];

                    instrumentParam.Value = bar.Instrument;
                    timeframeParam.Value = bar.Timeframe;
                    timestampUtcParam.Value = bar.Timestamp; // TODO - timestamp utc??
                    sessionDateParam.Value = bar.SessionDate;
                    openParam.Value = bar.Open;
                    closeParam.Value = bar.Close;
                    highParam.Value = bar.High;
                    lowParam.Value = bar.Low;
                    volumeParam.Value = bar.Volume;
                    bidVolumeParam.Value = bar.BidVolume;
                    askVolumeParam.Value = bar.AskVolume;
                    deltaParam.Value = bar.Delta;
                    cumulativeDeltaParam.Value = bar.CumulativeDelta;
                    vwapParam.Value = bar.VWAP;
                    atr14Param.Value = bar.ATR_14;
                    trueRangeParam.Value = bar.TrueRange;
                    realizedVol20Param.Value = bar.RealizedVol_20;
                    rsi14Param.Value = bar.RSI_14;
                    rsi7Param.Value = bar.RSI_7;
                    macdParam.Value = bar.MACD;
                    macdSignalParam.Value = bar.MACDSignal;
                    macdHistParam.Value = bar.MACDHist;
                    momentum10Param.Value = bar.Momentum_10;
                    ema9Param.Value = bar.EMA_9;
                    ema21Param.Value = bar.EMA_21;
                    emaSpreadParam.Value = bar.EMASpread;
                    adx14Param.Value = bar.ADX_14;
                    bbUpperParam.Value = bar.BB_Upper;
                    bbLowerParam.Value = bar.BB_Lower;
                    bbWidthParam.Value = bar.BB_Width;
                    percentBParam.Value = bar.PercentB;
                    highLowRangeParam.Value = bar.HighLowRange;
                    closePositionParam.Value = bar.ClosePosition;
                    bodySizeParam.Value = bar.BodySize;
                    upperWickParam.Value = bar.UpperWick;
                    lowerWickParam.Value = bar.LowerWick;
                    return1Param.Value = bar.Return_1;
                    return3Param.Value = bar.Return_3;
                    return5Param.Value = bar.Return_5;
                    return10Param.Value = bar.Return_10;
                    minutesFromOpenParam.Value = bar.MinutesFromOpen;
                    isOpeningParam.Value = bar.IsOpening;
                    isMidDayParam.Value = bar.IsMidDay;
                    isClosingParam.Value = bar.IsClosing;
                    futureReturn1Param.Value = bar.FutureReturn_1;
                    futureReturn3Param.Value = bar.FutureReturn_3;
                    futureReturn3Param.Value = bar.FutureReturn_5;
                    futureReturn10Param.Value = bar.FutureReturn_10;
                    labelUp1Param.Value = bar.Label_Up_1;
                    labelUp3Param.Value = bar.Label_Up_3;
                    labelUp5Param.Value = bar.Label_Up_5;
                    labelUp10Param.Value = bar.Label_Up_10;

                    await command.ExecuteNonQueryAsync();
                }

                transaction.Commit();
                total += batch.Count;
            }
        }
        else
        {
            using var transaction = connection.BeginTransaction();
            var command = connection.CreateCommand();
            command.CommandText = barDataInsert;

            var instrumentParam = command.CreateParameter(); instrumentParam.ParameterName = "$Instrument"; command.Parameters.Add(instrumentParam);
            var timeframeParam = command.CreateParameter(); timeframeParam.ParameterName = "$Timeframe"; command.Parameters.Add(timeframeParam);
            var timestampUtcParam = command.CreateParameter(); timestampUtcParam.ParameterName = "$TimestampUtc"; command.Parameters.Add(timestampUtcParam);
            var sessionDateParam = command.CreateParameter(); sessionDateParam.ParameterName = "$SessionDate"; command.Parameters.Add(sessionDateParam);
            var openParam = command.CreateParameter(); openParam.ParameterName = "$Open"; command.Parameters.Add(openParam);
            var closeParam = command.CreateParameter(); closeParam.ParameterName = "$Close"; command.Parameters.Add(closeParam);
            var highParam = command.CreateParameter(); highParam.ParameterName = "$High"; command.Parameters.Add(highParam);
            var lowParam = command.CreateParameter(); lowParam.ParameterName = "$Low"; command.Parameters.Add(lowParam);
            var volumeParam = command.CreateParameter(); volumeParam.ParameterName = "$Volume"; command.Parameters.Add(volumeParam);
            var bidVolumeParam = command.CreateParameter(); bidVolumeParam.ParameterName = "$BidVolume"; command.Parameters.Add(bidVolumeParam);
            var askVolumeParam = command.CreateParameter(); askVolumeParam.ParameterName = "$AskVolume"; command.Parameters.Add(askVolumeParam);
            var deltaParam = command.CreateParameter(); deltaParam.ParameterName = "$Delta"; command.Parameters.Add(deltaParam);
            var cumulativeDeltaParam = command.CreateParameter(); cumulativeDeltaParam.ParameterName = "$CumulativeDelta"; command.Parameters.Add(cumulativeDeltaParam);
            var vwapParam = command.CreateParameter(); vwapParam.ParameterName = "$VWAP"; command.Parameters.Add(vwapParam);
            var atr14Param = command.CreateParameter(); atr14Param.ParameterName = "$ATR_14"; command.Parameters.Add(atr14Param);
            var trueRangeParam = command.CreateParameter(); trueRangeParam.ParameterName = "$TrueRange"; command.Parameters.Add(trueRangeParam);
            var realizedVol20Param = command.CreateParameter(); realizedVol20Param.ParameterName = "$RealizedVol_20"; command.Parameters.Add(realizedVol20Param);
            var rsi14Param = command.CreateParameter(); rsi14Param.ParameterName = "$RSI_14"; command.Parameters.Add(rsi14Param);
            var rsi7Param = command.CreateParameter(); rsi7Param.ParameterName = "$RSI_7"; command.Parameters.Add(rsi7Param);
            var macdParam = command.CreateParameter(); macdParam.ParameterName = "$MACD"; command.Parameters.Add(macdParam);
            var macdSignalParam = command.CreateParameter(); macdSignalParam.ParameterName = "$MACDSignal"; command.Parameters.Add(macdSignalParam);
            var macdHistParam = command.CreateParameter(); macdHistParam.ParameterName = "$MACDHist"; command.Parameters.Add(macdHistParam);
            var momentum10Param = command.CreateParameter(); momentum10Param.ParameterName = "$Momentum_10"; command.Parameters.Add(momentum10Param);
            var ema9Param = command.CreateParameter(); ema9Param.ParameterName = "$EMA_9"; command.Parameters.Add(ema9Param);
            var ema21Param = command.CreateParameter(); ema21Param.ParameterName = "$EMA_21"; command.Parameters.Add(ema21Param);
            var emaSpreadParam = command.CreateParameter(); emaSpreadParam.ParameterName = "$EMASpread"; command.Parameters.Add(emaSpreadParam);
            var adx14Param = command.CreateParameter(); adx14Param.ParameterName = "$ADX_14"; command.Parameters.Add(adx14Param);
            var bbUpperParam = command.CreateParameter(); bbUpperParam.ParameterName = "$BB_Upper"; command.Parameters.Add(bbUpperParam);
            var bbLowerParam = command.CreateParameter(); bbLowerParam.ParameterName = "$BB_Lower"; command.Parameters.Add(bbLowerParam);
            var bbWidthParam = command.CreateParameter(); bbWidthParam.ParameterName = "$BB_Width"; command.Parameters.Add(bbWidthParam);
            var percentBParam = command.CreateParameter(); percentBParam.ParameterName = "$PercentB"; command.Parameters.Add(percentBParam);
            var highLowRangeParam = command.CreateParameter(); highLowRangeParam.ParameterName = "$HighLowRange"; command.Parameters.Add(highLowRangeParam);
            var closePositionParam = command.CreateParameter(); closePositionParam.ParameterName = "$ClosePosition"; command.Parameters.Add(closePositionParam);
            var bodySizeParam = command.CreateParameter(); bodySizeParam.ParameterName = "$BodySize"; command.Parameters.Add(bodySizeParam);
            var upperWickParam = command.CreateParameter(); upperWickParam.ParameterName = "$UpperWick"; command.Parameters.Add(upperWickParam);
            var lowerWickParam = command.CreateParameter(); lowerWickParam.ParameterName = "$LowerWick"; command.Parameters.Add(lowerWickParam);
            var return1Param = command.CreateParameter(); return1Param.ParameterName = "$Return_1"; command.Parameters.Add(return1Param);
            var return3Param = command.CreateParameter(); return3Param.ParameterName = "$Return_3"; command.Parameters.Add(return3Param);
            var return5Param = command.CreateParameter(); return5Param.ParameterName = "$Return_5"; command.Parameters.Add(return5Param);
            var return10Param = command.CreateParameter(); return10Param.ParameterName = "$Return_10"; command.Parameters.Add(return10Param);
            var minutesFromOpenParam = command.CreateParameter(); minutesFromOpenParam.ParameterName = "$MinutesFromOpen"; command.Parameters.Add(minutesFromOpenParam);
            var isOpeningParam = command.CreateParameter(); isOpeningParam.ParameterName = "$IsOpening"; command.Parameters.Add(isOpeningParam);
            var isMidDayParam = command.CreateParameter(); isMidDayParam.ParameterName = "$IsMidDay"; command.Parameters.Add(isMidDayParam);
            var isClosingParam = command.CreateParameter(); isClosingParam.ParameterName = "$IsClosing"; command.Parameters.Add(isClosingParam);
            var futureReturn1Param = command.CreateParameter(); futureReturn1Param.ParameterName = "$FutureReturn_1"; command.Parameters.Add(futureReturn1Param);
            var futureReturn3Param = command.CreateParameter(); futureReturn3Param.ParameterName = "$FutureReturn_3"; command.Parameters.Add(futureReturn3Param);
            var futureReturn5Param = command.CreateParameter(); futureReturn5Param.ParameterName = "$FutureReturn_5"; command.Parameters.Add(futureReturn5Param);
            var futureReturn10Param = command.CreateParameter(); futureReturn10Param.ParameterName = "$FutureReturn_10"; command.Parameters.Add(futureReturn10Param);
            var labelUp1Param = command.CreateParameter(); labelUp1Param.ParameterName = "$Label_Up_1"; command.Parameters.Add(labelUp1Param);
            var labelUp3Param = command.CreateParameter(); labelUp3Param.ParameterName = "$Label_Up_3"; command.Parameters.Add(labelUp3Param);
            var labelUp5Param = command.CreateParameter(); labelUp5Param.ParameterName = "$Label_Up_5"; command.Parameters.Add(labelUp5Param);
            var labelUp10Param = command.CreateParameter(); labelUp10Param.ParameterName = "$Label_Up_10"; command.Parameters.Add(labelUp10Param);

            for (var i = 0; i < insert.Count; i++)
            {
                var bar = insert[i];
                instrumentParam.Value = bar.Instrument;
                timeframeParam.Value = bar.Timeframe;
                timestampUtcParam.Value = bar.Timestamp; // TODO - timestamp utc??
                sessionDateParam.Value = bar.SessionDate;
                openParam.Value = bar.Open;
                closeParam.Value = bar.Close;
                highParam.Value = bar.High;
                lowParam.Value = bar.Low;
                volumeParam.Value = bar.Volume;
                bidVolumeParam.Value = bar.BidVolume;
                askVolumeParam.Value = bar.AskVolume;
                deltaParam.Value = bar.Delta;
                cumulativeDeltaParam.Value = bar.CumulativeDelta;
                vwapParam.Value = bar.VWAP;
                atr14Param.Value = bar.ATR_14;
                trueRangeParam.Value = bar.TrueRange;
                realizedVol20Param.Value = bar.RealizedVol_20;
                rsi14Param.Value = bar.RSI_14;
                rsi7Param.Value = bar.RSI_7;
                macdParam.Value = bar.MACD;
                macdSignalParam.Value = bar.MACDSignal;
                macdHistParam.Value = bar.MACDHist;
                momentum10Param.Value = bar.Momentum_10;
                ema9Param.Value = bar.EMA_9;
                ema21Param.Value = bar.EMA_21;
                emaSpreadParam.Value = bar.EMASpread;
                adx14Param.Value = bar.ADX_14;
                bbUpperParam.Value = bar.BB_Upper;
                bbLowerParam.Value = bar.BB_Lower;
                bbWidthParam.Value = bar.BB_Width;
                percentBParam.Value = bar.PercentB;
                highLowRangeParam.Value = bar.HighLowRange;
                closePositionParam.Value = bar.ClosePosition;
                bodySizeParam.Value = bar.BodySize;
                upperWickParam.Value = bar.UpperWick;
                lowerWickParam.Value = bar.LowerWick;
                return1Param.Value = bar.Return_1;
                return3Param.Value = bar.Return_3;
                return5Param.Value = bar.Return_5;
                return10Param.Value = bar.Return_10;
                minutesFromOpenParam.Value = bar.MinutesFromOpen;
                isOpeningParam.Value = bar.IsOpening;
                isMidDayParam.Value = bar.IsMidDay;
                isClosingParam.Value = bar.IsClosing;
                futureReturn1Param.Value = bar.FutureReturn_1;
                futureReturn3Param.Value = bar.FutureReturn_3;
                futureReturn3Param.Value = bar.FutureReturn_5;
                futureReturn10Param.Value = bar.FutureReturn_10;
                labelUp1Param.Value = bar.Label_Up_1;
                labelUp3Param.Value = bar.Label_Up_3;
                labelUp5Param.Value = bar.Label_Up_5;
                labelUp10Param.Value = bar.Label_Up_10;

                await command.ExecuteNonQueryAsync();
            }

            transaction.Commit();
            total += insert.Count;
        }

        return total;
    }

    #endregion
}