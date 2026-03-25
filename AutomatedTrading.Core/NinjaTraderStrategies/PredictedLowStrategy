#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using Newtonsoft.Json;
#endregion

#region README IMPORTANT

/*
    This strategy calls the AutomatedTrading.Core.Api at session open to retrieve
    the predicted LOW level for the session. It trades a long reversal near that
    level, and will flatten any open short if price reaches the predicted low.

    Designed for RTH 1-minute bars (8:30 AM – 3:15 PM CT). Ensure the API is
    running before the strategy is enabled.

    Stop loss is anchored to the predicted level, not the entry price. If price
    breaks through the predicted low by StopLossPoints, the prediction has
    failed and the position is closed with a bounded loss.

    Entries:
      Near predicted LOW → EnterLong (or ExitShort to flatten)

    Proximity and stop parameters are tunable via the strategy properties panel.
*/

#endregion

//This namespace holds Strategies in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Strategies
{
    public class PredictedLowStrategy : Strategy
    {
        // =====================================================================
        // Constants
        // =====================================================================
        private const string API_URL     = "http://localhost:5244";
        private const string STRATEGY_ID = "PredictedLow";
        private const string ENTRY_LONG  = "PL_Long";
        private const string ENTRY_SHORT = "PL_Short";

        // =====================================================================
        // Session state
        // =====================================================================
        private double predictedLow;
        private bool   inferenceReady;
        private bool   hasLowHit;

        // =====================================================================
        // Parameters
        // =====================================================================

        /// <summary>
        /// How close (in points) price must get to the predicted low to trigger
        /// an entry or flatten. Tighter = less risk, fewer triggers.
        /// </summary>
        [NinjaScriptProperty]
        [Range(0.25, double.MaxValue)]
        [Display(Name = "Proximity Points", Order = 1, GroupName = "Levels")]
        public double ProximityPoints { get; set; }

        /// <summary>
        /// How far (in points) beyond the predicted low the stop is placed.
        /// The stop is anchored to the predicted low, not the entry price,
        /// so maximum risk is ProximityPoints + StopLossPoints per contract.
        /// </summary>
        [NinjaScriptProperty]
        [Range(0.25, double.MaxValue)]
        [Display(Name = "Stop Loss Points", Order = 2, GroupName = "Levels")]
        public double StopLossPoints { get; set; }

        // =====================================================================
        // Lifecycle
        // =====================================================================
        protected override void OnStateChange()
        {
            switch (State)
            {
                case State.SetDefaults:
                    Name                          = STRATEGY_ID;
                    Calculate                     = Calculate.OnBarClose;
                    EntriesPerDirection           = 1;
                    IsExitOnSessionCloseStrategy  = true;
                    ExitOnSessionCloseSeconds     = 30;
                    BarsRequiredToTrade           = 1;

                    ProximityPoints = 3.0;
                    StopLossPoints  = 5.0;

                    inferenceReady = false;
                    predictedLow   = double.MinValue;
                    hasLowHit      = false;
                    break;

                default:
                    break;
            }
        }

        // =====================================================================
        // Bar update
        // =====================================================================
        protected override void OnBarUpdate()
        {
            if (Bars.IsFirstBarOfSession)
            {
                inferenceReady = false;
                predictedLow   = double.MinValue;
                hasLowHit      = false;
                fetchPrediction(Time[0], Open[0]);
            }

            if (!inferenceReady)
                return;

            var position = Position.MarketPosition;

            // ─── Near predicted LOW: go long or flatten short ────────────────
            if (Low[0] <= predictedLow + ProximityPoints)
            {
                if (position == MarketPosition.Short)
                {
                    ExitShort(ENTRY_SHORT);
                    Print($"{Time[0]:HH:mm} PL: Flattening short — price reached predicted low ({predictedLow:F2})");
                }
                else if (position == MarketPosition.Flat && !hasLowHit)
                {
                    SetStopLoss(ENTRY_LONG, CalculationMode.Price, predictedLow - StopLossPoints, false);
                    EnterLong(ENTRY_LONG);
                    Print($"{Time[0]:HH:mm} PL: EnterLong (~current:{Close[0]}) near predicted low ({predictedLow:F2}), stop {predictedLow - StopLossPoints:F2}");
                }
                hasLowHit = true;
            }
        }

        // =====================================================================
        // API call
        // =====================================================================
        private void fetchPrediction(DateTime barTime, double sessionOpen)
        {
            var sessionDate = barTime.ToString("yyyyMMdd");
            var requestBody = JsonConvert.SerializeObject(new
            {
                sessionDate = sessionDate,
                sessionOpen = sessionOpen
            });
            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);

                    var response = client.PostAsync($"{API_URL}/inference/bar-low", content)
                        .GetAwaiter().GetResult();

                    if (!response.IsSuccessStatusCode)
                    {
                        Print($"PL: bar-low API returned {(int)response.StatusCode} for {sessionDate}");
                        return;
                    }

                    var json   = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    var result = JsonConvert.DeserializeObject<BarLowApiResponse>(json);
                    if (result != null)
                        predictedLow = result.PredictedLow;
                }

                inferenceReady = true;
                Print($"PL {sessionDate}: predictedLow={predictedLow:F2}  open={sessionOpen:F2}");
            }
            catch (Exception ex)
            {
                Print($"PL: API call failed — {ex.Message}");
                inferenceReady = false;
            }
        }

        // =====================================================================
        // API response POCO
        // =====================================================================
        private class BarLowApiResponse
        {
            [JsonProperty("predictedLow")]
            public double PredictedLow { get; set; }

            [JsonProperty("predictedMove")]
            public double PredictedMove { get; set; }
        }
    }
}
