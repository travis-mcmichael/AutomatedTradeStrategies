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
	public class BarCollectionStrategy : Strategy
	{
		private IBarRepository barRepository;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name = "BarCollectorStrategy";
				Calculate = Calculate.OnBarClose;
			}
			else if (State == State.Configure)
			{
				barRepository = new BarRepository(new SqliteConnectionFactory(AppConstants.SQLITE_CONNECTION));
			}
		}

		protected override void OnBarUpdate()
		{
			var barTime     = Time[0];
			var sessionDate = barTime.Date;
			if (barTime.Hour >= 16)
				sessionDate = sessionDate.AddDays(1);

			var bar = new BarData
			{
				Instrument  = Instrument.FullName,
				Timeframe   = FormatTimeframe(Bars.BarsPeriod),
				Timestamp   = barTime,
				SessionDate = $"{sessionDate:yyyyMMdd}",
				SessionTime = $"{barTime:HHmmss}",
				Open        = Open[0],
				High        = High[0],
				Low         = Low[0],
				Close       = Close[0],
				Volume      = Volume[0]
			};

			_ = InsertBarAsync(bar);
		}

		private async Task InsertBarAsync(BarData bar)
		{
			try
			{
				await barRepository.InsertBatchAsync(new List<BarData> { bar }, 1);
			}
			catch (Exception ex)
			{
				Print($"Error inserting bar: {ex.Message}" + ex.InnerException?.Message ?? "");
			}
		}

		private static string FormatTimeframe(BarsPeriod period)
		{
			switch (period.BarsPeriodType)
			{
				case BarsPeriodType.Minute: return $"{period.Value}m";
				case BarsPeriodType.Second: return $"{period.Value}s";
				case BarsPeriodType.Tick:   return $"{period.Value}t";
				case BarsPeriodType.Day:    return $"{period.Value}d";
				case BarsPeriodType.Week:   return $"{period.Value}w";
				case BarsPeriodType.Month:  return $"{period.Value}mo";
				default:                   return $"{period.Value}{period.BarsPeriodType.ToString().ToLower()}";
			}
		}
	}
}
