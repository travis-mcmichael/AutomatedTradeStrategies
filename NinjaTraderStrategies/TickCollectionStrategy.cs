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

using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.DataLayer.Sqlite;
#endregion

//This namespace holds Strategies in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Strategies
{
	public class TickCollectionStrategy : Strategy
	{
		private readonly List<TickData> tickBatch = new List<TickData>();
		private ITickRepository tickRepository;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name = "TickCollectorStrategy";
			}
			else if (State == State.Configure)
			{
				tickRepository = new TickRepository(new SqliteConnectionFactory(AppConstants.SQLITE_CONNECTION));
			}
			else if (State == State.Terminated)
			{
				if (tickBatch.Count > 0)
				{
					var remainingBatch = new List<TickData>(tickBatch);
					tickBatch.Clear();
					_ = FlushBatchAsync(remainingBatch);
				}
			}
		}

		protected override void OnMarketData(MarketDataEventArgs e)
		{
			if (e.MarketDataType != MarketDataType.Last) return;

			long tickTime = new DateTimeOffset(e.Time).ToUnixTimeMilliseconds();

			var tick = new TickData
			{
				Instrument = Instrument.FullName,
				TickTime   = tickTime,
				LastPrice  = e.Price,
				Volume     = e.Volume,
				Bid        = e.Bid,
				Ask        = e.Ask
			};

			tickBatch.Add(tick);

			if (tickBatch.Count >= AppConstants.TICK_DATA_BATCH_SIZE)
			{
				var batchCopy = new List<TickData>(tickBatch);
				tickBatch.Clear();
				_ = FlushBatchAsync(batchCopy);
			}
		}

		protected override void OnBarUpdate() { }

		private async Task FlushBatchAsync(List<TickData> batch)
		{
			try
			{
				await tickRepository.InsertBatchAsync(batch, BatchSize);
			}
			catch (Exception ex)
			{
				Print($"Error inserting tick batch: {ex.Message}");
			}
		}
	}
}
