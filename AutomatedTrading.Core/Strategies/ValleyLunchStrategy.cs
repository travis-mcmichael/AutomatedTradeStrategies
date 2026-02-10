#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
#endregion

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies
{
	public class ValleyLunchStrategy : Strategy
	{

#region - Constants -

		protected const double DOLLARS_PER_POINT = 5.0;
		protected const string STRATEGY_ID = "ValleyLunch";
	
#endregion

#region - Strategy Parameters -
		
		/// <summary>
		/// The time when the strategy needs to start tracking data.
		/// </summary>
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Tracking Start Time", Order = 1)]
		public int TrackingStart { get; set; }
		
		/// <summary>
		/// The time when the strategy needs to stop tracking data.
		/// </summary>
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Tracking End Time", Order = 2)]
		public int TrackingEnd { get; set; }		

		/// <summary>
		/// The time when the strategy is allowed to enter a trade.
		/// </summary>
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Trade Start Time", Order = 3)]
		public int TradeStart { get; set; }
		
		/// <summary>
		/// The time when the strategy is no longer allowed to enter a trade.
		/// </summary>
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Trade End Time", Order = 4)]
		public int TradeEnd { get; set; }
		
		/// <summary>
		/// The starting stop-loss value when entering a trade.
		/// </summary>
		[NinjaScriptProperty]
		[Range(1, double.MaxValue)]
		[Display(Name = "Initial Stop Loss", Order = 5)]
		public double InitialStopLoss { get; set; }
		
		/// <summary>
		/// The profit amount to trigger the logic to update the stop-loss amount. 
		/// </summary>
		[NinjaScriptProperty]
		[Range(1, double.MaxValue)]
		[Display(Name = "Trail Trigger", Order = 6)]
		public double TrailTrigger { get; set; }
		/// <summary>
		/// The increments used when updating the stop-loss amount. 
		/// </summary>
		[NinjaScriptProperty]
		[Range(1, double.MaxValue)]
		[Display(Name = "Trail Distance", Order = 7)]
		public double TrailDistance { get; set; }
		
		/// <summary>
		/// The number of times the price has to hit a valley before entering a trade. 
		/// </summary>
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Trade-Window Valley Threshold", Order = 8)]
		public int TradeWindowValleyThreshold { get; set; }
		
#endregion

#region - Protected Fields -

		protected DateTime startDate;
		protected bool startCaptured;
		protected double sessionLow;
		protected int sessionLowTime;
		protected int sessionLowestHitCount;
		protected bool keepGoing;
		protected PeakValleyRecord peakValley;

	#region - Trailing Stop -
		protected double highestPrice;
		protected double currentStopPrice;
		protected int trailTriggers;
		protected bool success;
		
		private void setTrailingStopDefaults()
		{
			highestPrice = 0;
			trailTriggers = 0;
			currentStopPrice = 0;
			success = false;
		}
		
		private void checkStopLoss()
		{
			highestPrice = Math.Max(highestPrice, High[0]);
			if (currentStopPrice == 0)
			{
				double stopPoints = InitialStopLoss / DOLLARS_PER_POINT;
				currentStopPrice = Position.AveragePrice;
			}
			
			double profitPoints = highestPrice - Position.AveragePrice;
			double profitDollars = profitPoints * DOLLARS_PER_POINT;
			
			if (profitDollars >= TrailTrigger)
			{
				double trailDistPoints = TrailDistance / DOLLARS_PER_POINT;
				double newStop = highestPrice - TrailDistance;
				if (newStop > currentStopPrice)
				{
					currentStopPrice = newStop;
					success = true;
					trailTriggers++;
					SetStopLoss(CalculationMode.Price, currentStopPrice);
				}
			}
		}
	#endregion
		
#endregion
		
#region - Override Methods -

		/// <summary>
		/// Switch on State:
		///		State.SetDefaults - sets Strategy defaults
		///		State.Terminated - log summary line if there were any trades made during the session.
		///		Default - do nothing
		/// </summary>
		protected override void OnStateChange() 
		{
			switch (State)
			{
				case State.SetDefaults:
					Name = STRATEGY_ID;
					Calculate = Calculate.OnBarClose;
					EntriesPerDirection = 1;
					IsExitOnSessionCloseStrategy = true;
					startCaptured = false;
					startDate = new DateTime();
					setTrailingStopDefaults();
					
					peakValley = new PeakValleyRecord();
					peakValley.Initialize();
					break;
				case State.Terminated:
					var summary = OutputLogBuilder.GetStrategySummaryLine(startDate, TrackingStart, TrackingEnd, TradeStart, TradeEnd, TradeWindowValleyThreshold, SystemPerformance.AllTrades, trailTriggers);
					if (!string.IsNullOrEmpty(summary))
						Print(summary);
					break;
				default:
					break;
			}
		}
		
		/// <summary>
		///	Checks if the window for tracking data has started,
		/// checks trade conditions, enters trades, and updates stop-loss values if necessary
		/// </summary>
		protected override void OnBarUpdate()
		{
			int currentTime = ToTime(Time[0]);
			
			// Reset defaults on first bar of session
			if (Bars.IsFirstBarOfSession)
			{
				resetTrackingData();
				return;
			}
			
			// Not enough data to start
			if (CurrentBar < 2)
				return;
			
			// Only track data during tracking window.
			// NOTE - this was implemented to keep wonky overnight data from messing with daytime tracking logic.
			if (currentTime < TrackingStart || currentTime > TrackingEnd)
				return;
			
			if (!startCaptured)
			{
				startDate = Time[0].Date;
				startCaptured = true;
			}
			
			// Monitor session low price + related info.
			peakValley.OnBarUpdate(CurrentBar, currentTime, High, Low);
			if (!string.IsNullOrEmpty(peakValley.Message))
			{
				Print(peakValley.Message);
			}
			
			handleSessionLowestDetection(currentTime);
			
			// If you're flat and the entry trade conditions pass, enter market
			if (Position.MarketPosition == MarketPosition.Flat && checkTradeEntryConditions(Time[0]) && keepGoing)
			{
				EnterLong(STRATEGY_ID);
			}
			
			// If you're alrea
			if (Position.MarketPosition == MarketPosition.Long)
			{
				checkStopLoss();
			}
		}
		
		/// <summary>
		/// If the stop-loss is hit, reset the sessionLow trackers.
		/// </summary>
		protected override void OnExecutionUpdate(
			Execution execution,
			string executionId,
			double price,
			int quantity,
			MarketPosition marketPosition,
			string orderId,
			DateTime time)
		{
			if (execution.Order == null)
				return;
			if (execution.Order.OrderState != OrderState.Filled)
				return;
			if (execution.Order.OrderType == OrderType.StopMarket)
			{
				Print(OutputLogBuilder.GetStopLossMessageLine(price, Time[0]));
				if (success) 
				{
					success = false;
				}
				else 
				{
					keepGoing = false;
				}
			}
		}
		
#endregion

#region - Private Helper Methods -

		private void resetTrackingData()
		{
			int currentTime = ToTime(Time[0]);
			sessionLow = -1;
			sessionLowTime = ToTime(Time[0]);
			sessionLowestHitCount = 0;
			keepGoing = true;
			peakValley.Initialize();
			
			setTrailingStopDefaults();
		}
		
		private bool checkTradeEntryConditions(DateTime time) 
		{
			int currentTime = ToTime(time);
			bool inEntryWindow = currentTime >= TradeStart && currentTime <= TradeEnd;
			bool sessionLowInWindow = sessionLowTime >= TradeStart && sessionLowTime <= TradeEnd;
			bool hasTradeWindowValleyThresholdReached = peakValley.ValleyPrices.Keys.Where(x => x >= TradeStart).Count() >= TradeWindowValleyThreshold;
			//bool hasSessionLowestHitThresholdReached = sessionLowestHitCount >= SessionLowestHitThreshold;
			
			Dictionary<string, bool> conditions = new Dictionary<string, bool>();
			conditions.Add("InEntryWindow", inEntryWindow);
			conditions.Add("SessionLowInWindow", sessionLowInWindow);
			conditions.Add("HasTradeWindowValleyThresholdReached", hasTradeWindowValleyThresholdReached);
			
			var debugLine = OutputLogBuilder.GetBarUpdateOutputLine(time, STRATEGY_ID, conditions, sessionLow, sessionLowestHitCount, Close[0], success, keepGoing);
			if (!string.IsNullOrEmpty(debugLine))
			{
				Print(debugLine);
			}
			
			return conditions.All(x => x.Value);
		}
		
		private void handleSessionLowestDetection(int currentTime)
		{
			if (applyLowestHitDetectionInstantaneously())
			{
				sessionLow = Low[0];
				sessionLowTime = currentTime;
				if (currentTime >= TradeStart && currentTime <= TradeEnd)
				{
					//sessionLowestHitCount++;
					logSessionLowestHit();
				}
			}
		}
		
		private bool applyLowestHitDetectionInstantaneously()
		{
			return Low[0] < sessionLow || sessionLow < 0;
		}
		
		private void logSessionLowestHit() 
		{
			Print($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Session Lowest hit # {sessionLowestHitCount} @ {sessionLow}");
		}

#endregion

	}
}
