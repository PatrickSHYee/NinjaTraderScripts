/*
*	Indicator: VWAP
*	Author: Patrick S. Yee
*	Version: 0.1
*
*	Description: An indicator that was scripted in NinjaTrader 7.  Needs to be scripted for NinjaTrader 8.
*/
#region Using declarations
using System;
using System.Collections;
using System.Diagnostics;
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
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

public enum BandType
{
    AvgVWAP,
    VWAP,
}

//This namespace holds Indicators in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Indicators
{
    public class VWAP : Indicator
    {
        #region Variables
        // VWAP Variables
        private int numStdDev = 3;
        public double SD = 0;
        private double sd1Multi = 1;
        private double sd2Multi = 2;
        private double sd3Multi = 3;

        private BandType bandType = BandType.VWAP;
        private bool showVwapstddev = true;
        private bool showWarning = true;
        private bool estVwap = true;
        private Brush fontColor;
        private double previousVolume = 0;
        private double currentVolume = 0;
        private double tickVolume = 0;
        private double vwapavg = 0;
        private double vwapSum = 0;
        private ArrayList volumeArray = new ArrayList();
        private double volwap = 0;
        private ArrayList priceArray = new ArrayList();
        private ArrayList vwapArray = new ArrayList();

        private ArrayList priceRangeArray = new ArrayList();
        private ArrayList priceRangeVolumeArray = new ArrayList();

        private Series<double> vwapSeries;
        private DateTime loadTime;
        private DateTime sessionEnd;
        private int previousBar = 0;
        private int timeCheck = 1;

        private TimeSpan startTime;
        private DateTime startTimeDate;
        private TimeSpan endTime;
        private DateTime endTimeDate;
        private bool useSessionBegin = false;

        // Plot Coloring
        private Brush bandAreaColor = Brushes.Blue;  // NS7 - private Color bandAreaColor = Color.Blue;
        private int bandAreaColorOpacity = 1;

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"VWAP is the ratio of the value traded to total volume traded over a particular time horizon (usually one day). It is a measure of the average price a stock traded at over the trading horizon.";
                Name = "VWAP (Volume-Weighted Average Price)";
                Calculate = Calculate.OnEachTick;                                       // NS 7, this state was set to false. ie: CalculateOnBarClose = false;
                IsOverlay = true;
                //DisplayInDataBox					= true;                             // NS7, this is not set
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines = true;
                PaintPriceMarkers = true;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsAutoScale = false;
                BarsRequiredToPlot = 1;                                                 // NS7 - BarsRequired = 1;
                                                                                        //PriceTypeSupported = true;          // default set to true.
                                                                                        //Disable this property if your indicator requires custom values that cumulate with each new market data event. 
                                                                                        //See Help Guide for additional information.
                IsSuspendedWhileInactive = true;
            }
            else if (State == State.Configure)
            {
                AddPlot(Brushes.Orange, "VWAP");
                AddPlot(bandAreaColor, "SD1 Upper");
                AddPlot(bandAreaColor, "SD1 Lower");
                AddPlot(bandAreaColor, "SD2 Upper");
                AddPlot(bandAreaColor, "SD2 Lower");
                AddPlot(bandAreaColor, "SD3 Upper");
                AddPlot(bandAreaColor, "SD3 Lower");
                // Dataseries
                vwapSeries = new Series<double>(this);
            }
        }

        /// <summary>
        /// Called on each bar update event (incoming tick)s
        /// </summary>
        protected override void OnBarUpdate()
        {
            if (Bars == null)
                return;
            //Only allowed on Intraday charts.
            if (BarsPeriod.BarsPeriodType == BarsPeriodType.Day || BarsPeriod.BarsPeriodType == BarsPeriodType.Week || BarsPeriod.BarsPeriodType == BarsPeriodType.Month || BarsPeriod.BarsPeriodType == BarsPeriodType.Year)
                return;
            //Remembers the DateTime that the indicator was started on
            if (CurrentBar == 0)
            {
                if (Bars.BarsSeries.IsMarketDataSubscribed)
                    loadTime = Now;
                else
                    loadTime = DateTime.Now;

                sessionEnd = new DateTime(Bars.SessionIterator.ActualSessionBegin.Year, Bars.SessionIterator.ActualSessionBegin.Month, Bars.SessionIterator.ActualSessionBegin.Day, Bars.SessionIterator.ActualSessionBegin.Hour, Bars.SessionIterator.ActualSessionBegin.Minute, Bars.SessionIterator.ActualSessionBegin.Second);
                startTimeDate = new DateTime(Bars.SessionIterator.ActualSessionBegin.Year, Bars.SessionIterator.ActualSessionBegin.Month, Bars.SessionIterator.ActualSessionBegin.Day, startTime.Hours, startTime.Minutes, startTime.Seconds);
                endTimeDate = new DateTime(Bars.SessionIterator.ActualSessionBegin.Year, Bars.SessionIterator.ActualSessionBegin.Month, Bars.SessionIterator.ActualSessionBegin.Day, endTime.Hours, endTime.Minutes, endTime.Seconds);

                // Checks to ensure End Time is not before Start Time: if it is set End Time to SessionEnd Time
                if (DateTime.Compare(endTimeDate, startTimeDate) < 0)
                    endTimeDate = sessionEnd;

                // Determines whether to use SessionBegin Time or Start Time: If Start Time undefined/less than SessionBegin, use SessionBegin
                if (startTime.TotalMinutes == 0 || ToTime(startTimeDate) < ToTime(Bars.SessionIterator.ActualSessionBegin))
                    useSessionBegin = true;
            }

            // If Time is outside range of Start Time and End Time do not calculate further
            if (ToTime(Time[0]) < ToTime(startTimeDate) || (ToTime(endTimeDate) != 0 && ToTime(Time[0]) > ToTime(endTimeDate)))
                return;

            //Recalculates VWAP on start of every new session			
            if ((useSessionBegin && Bars.IsFirstBarOfSession && IsFirstTickOfBar) ||
                (useSessionBegin == false && IsFirstTickOfBar && ToTime(Time[1]) < ToTime(startTimeDate) && ToTime(Time[0]) >= ToTime(startTimeDate)))
            {
                estVwap = false;
                if (bandType == BandType.AvgVWAP) Calculate = Calculate.OnEachTick;
                timeCheck = 1;
                vwapSum = 0;
                vwapavg = 0;
                volwap = 0;
                volumeArray.Clear();
                priceArray.Clear();
                vwapArray.Clear();
                priceRangeArray.Clear();
                priceRangeVolumeArray.Clear();
            }
            //Checks to see if load time was before or after market start
            else if (loadTime > Time[0] && timeCheck == 1)
            {
                estVwap = true;
                timeCheck = 0;
            }
            else if (timeCheck == 1)
            {
                estVwap = false;
                if (bandType == BandType.AvgVWAP) Calculate = Calculate.OnEachTick;
                timeCheck = 0;
            }

            // Calculate current volume
            if (IsFirstTickOfBar)
            {
                tickVolume = Volume[0];
                previousVolume = Volume[0];
                currentVolume = Volume[0];

                previousBar = CurrentBar;
            }
            else
            {
                /* Check to see if this is a new bar. For some reason, FirstTickOfBar isn't reliable when using
				Market Replay to determine if it is a new bar. */
                if (previousBar == CurrentBar)
                    previousVolume = currentVolume;
                else
                    previousVolume = 0;

                previousBar = CurrentBar;
                currentVolume = Volume[0];
                tickVolume = currentVolume - previousVolume;
            }

            // Calculate VWAP. If using Avg VWAP for SD calcs, need to prevent heavy weighting on incoming ticks vs historical bars.
            if (estVwap && bandType == BandType.AvgVWAP)
            {
                Calculate = Calculate.OnBarClose;
                volumeArray.Add(tickVolume);
                priceArray.Add(Input[0]);
                volwap = vwap(volumeArray, priceArray);
                VWAPLine[0] = volwap;
            }
            else
            {
                volumeArray.Add(tickVolume);
                priceArray.Add(Input[0]);
                volwap = vwap(volumeArray, priceArray);
                VWAPLine[0] = volwap;
            }

            if (showVwapstddev)
            {
                switch (bandType)
                {
                    case BandType.AvgVWAP:
                        {
                            vwapArray.Add(volwap);
                            vwapSum = vwapSum + volwap;
                            vwapavg = vwapSum / vwapArray.Count;
                            SD = StdDeviation(vwapArray, vwapavg);
                            vwapSeries[0] = SD;
                            PlotSD(SD);
                            break;
                        }

                    case BandType.VWAP:
                        {
                            // Create array for price ranges (1 entry per traded price)
                            if (priceRangeArray.Contains(Input[0]))
                            {
                                int index = priceRangeArray.IndexOf(Input[0]);
                                double newVol = (double)priceRangeVolumeArray[index] + tickVolume;
                                priceRangeVolumeArray[index] = newVol;
                            }
                            else
                            {
                                priceRangeArray.Add(Input[0]);
                                priceRangeVolumeArray.Add(tickVolume);
                            }

                            SD = StdDeviationProb(priceRangeArray, volwap);
                            vwapSeries[0] = SD;
                            PlotSD(SD);
                            break;
                        }
                }
            }

            // Plotting
            if (estVwap && showWarning)
            {
                if (ChartControl != null)
                    //fontColor = ChartControl.GetAxisBrush(ChartControl.BackColor).Color;
                    fontColor = ChartControl.Properties.ChartText;
                else
                    fontColor = Brushes.Black;

                Draw.TextFixed(this, "VWARError", "VWAP is most accurate when loaded before market start. \nThe current VWAP values displayed are estimates.", TextPosition.BottomRight, fontColor, new NinjaTrader.Gui.Tools.SimpleFont("Arial", 8), Brushes.Transparent, Brushes.Transparent, 0);
            }
            else
                RemoveDrawObject("VWAPError");
        }

        #region Properties
        [Browsable(false)]      // Prevents the Series<double> from being displayed in the indicator properties dialog, DO NOT REMOVE
        [XmlIgnore()]           // Ensures that the indicator can be saved/recovered as part of a chart template, DO NOT REMOVE
        public Series<double> VWAPLine { get { return Values[0]; } }

        [Browsable(false)]      // Prevents the Series<double> from being displayed in the indicator properties dialog, DO NOT REMOVE
        [XmlIgnore()]           // Ensures that the indicator can be saved/recovered as part of a chart template, DO NOT REMOVE
        public Series<double> SD1Upper { get { return Values[1]; } }

        [Browsable(false)]      // Prevents the Series<double> from being displayed in the indicator properties dialog, DO NOT REMOVE
        [XmlIgnore()]           // Ensures that the indicator can be saved/recovered as part of a chart template, DO NOT REMOVE
        public Series<double> SD1Lower { get { return Values[2]; } }

        [Browsable(false)]      // Prevents the Series<double> from being displayed in the indicator properties dialog, DO NOT REMOVE
        [XmlIgnore()]           // Ensures that the indicator can be saved/recovered as part of a chart template, DO NOT REMOVE
        public Series<double> SD2Upper { get { return Values[3]; } }

        [Browsable(false)]      // Prevents the Series<double> from being displayed in the indicator properties dialog, DO NOT REMOVE
        [XmlIgnore()]           // Ensures that the indicator can be saved/recovered as part of a chart template, DO NOT REMOVE
        public Series<double> SD2Lower { get { return Values[4]; } }

        [Browsable(false)]      // Prevents the Series<double> from being displayed in the indicator properties dialog, DO NOT REMOVE
        [XmlIgnore()]           // Ensures that the indicator can be saved/recovered as part of a chart template, DO NOT REMOVE
        public Series<double> SD3Upper { get { return Values[5]; } }

        [Browsable(false)]      // Prevents the Series<double> from being displayed in the indicator properties dialog, DO NOT REMOVE
        [XmlIgnore()]           // Ensures that the indicator can be saved/recovered as part of a chart template, DO NOT REMOVE
        public Series<double> SD3Lower { get { return Values[6]; } }

        [Description("Std Dev 1 Multiplier")]
        [Display(Name="\t\t\tSD1 Multiplier", GroupName = "Plots", Order = 0)]
        public double SD1Multi { get { return sd1Multi; } set { sd1Multi = value; } }

        [Description("Std Dev 2 Multiplier")]
        [Display(Name="\t\tSD2 Muliplier", GroupName = "Plots", Order = 1)]
        public double SD2Multi { get { return sd2Multi; } set { sd2Multi = value; } }

        [Description("Std Dev 3 Muliplier")]
        [Display(Name = "\t\tSD3 Muliplier", GroupName = "Plots", Order = 2)]
        public double SD3Multi { get { return sd3Multi; } set { sd3Multi = value; } }

        [Description("Choose what to calculate Standard Deviation on.  Avgerage Volume-Weighted Average Price calculates standard deviation on input price with VWAP as the mean.")]
        [Display(Name="\t\tStd Dev Calc Mode", GroupName = "Settings", Order = 3)]
        public BandType BType { get { return bandType; } set { bandType = value; } }

        [Description("Show VWAP standard deviation.")]
        [Display(Name = "\t\t\tShow Std Dev", GroupName = "Settings", Order = 0)]
        public bool ShowVWAPStdDev { get { return showVwapstddev; } set { showVwapstddev = value; } }

        [Description("Number of Standard Deviation bands of VWAP. (max: 3)")]
        [Display(Name = "\t# of Std Dev", GroupName = "Settings", Order = 1)]
        public int NumStdDev { get { return numStdDev; } set { numStdDev = value; } }

        [Description("VWAP Start Time")]
        [Display(Name = "\t\t\t\t\t\tStartTime", GroupName = "Settings", Order = 2)]
        public TimeSpan StartTime { get { return startTime; } set { startTime = value; } }

        [Description("VWAP End Time")]
        [Display(Name = "\t\t\t\t\tEnd Time", GroupName = "Settings", Order = 3)]
        public TimeSpan EndTime { get { return endTime; } set { endTime = value; } }

        [Description("Show accuracy warning when potential inaccuracies exist.")]
        [Display(Name = "\t\t\t\tAccuracy Warning", GroupName = "Settings", Order = 4)]
        public bool ShowWarning { get { return showWarning; } set { showWarning = value; } }

        [Description("Band Color")]
        [Display(Name = "Band Area Color", GroupName = "Visual", Order = 0)]
        public Brush BandAreaColor { get { return bandAreaColor; } set { bandAreaColor = value; } }

        [Browsable(false)]
        public string BandAreaColorSerialize { get { return Serialize.BrushToString(bandAreaColor); } set { bandAreaColor = Serialize.StringToBrush(value); } }

        [Description("Band Color Opacity (least - most: 0 - 10)")]
        [Display(Name = "Color Opacity", GroupName = "Visual", Order = 1)]
        public int BandAreaColorOpacity { get { return bandAreaColorOpacity; } set { bandAreaColorOpacity = value; } }
        #endregion

        #region Miscellaneous
        private double vwap(ArrayList volume, ArrayList price)
        {
            int x = 0;
            double numerator = 0.0;
            double denominator = 0.0;

            while (x < volume.Count)
            {
                numerator = numerator + ((double)price[x] * (double)volume[x]);
                denominator = denominator + (double)volume[x];
                x++;
            }

            if (denominator > 0) return numerator / denominator;
            return 0;
        }

        private double StdDeviation(ArrayList array, double avg)
        {
            int x = 0;
            double sd = 0;

            while (x < array.Count)
            {
                sd += Math.Pow((double)array[x] - avg, 2);
                x++;
            }
            if (sd > 0) sd = Math.Sqrt(sd / array.Count);
            return sd;
        }

        public double StdDeviationProb(ArrayList array, double avg)
        {
            double totalVolume = 0;
            int x = 0;
            double sd = 0;

            for (int n = 0; n <= Bars.BarsSinceNewTradingDay; n++)
                totalVolume += Volume[n];

            while (x < priceRangeArray.Count)
            {
                int index = priceRangeArray.IndexOf(array[x]);

                if (totalVolume > 0)
                    sd += ((Math.Pow((double)array[x] - avg, 2) * ((double)priceRangeVolumeArray[index] / totalVolume)));
                x++;
            }
            if (sd > 0) sd = Math.Sqrt(sd);
            return sd;
        }

        private void PlotSD(double SD)
        {
            if (numStdDev == 3)
            {
                SD1Upper[0] = VWAPLine[0] + sd1Multi * SD;
                SD1Lower[0] = VWAPLine[0] - sd1Multi * SD;
                SD2Upper[0] = VWAPLine[0] + sd2Multi * SD;
                SD2Lower[0] = VWAPLine[0] - sd2Multi * SD;
                SD3Upper[0] = VWAPLine[0] + sd3Multi * SD;
                SD3Lower[0] = VWAPLine[0] - sd3Multi * SD;

                // Draw Regions
                Draw.Region(this, "ColorVWAP", CurrentBar, 0, SD1Upper, SD1Lower, Brushes.Transparent, bandAreaColor, bandAreaColorOpacity);
                Draw.Region(this, "ColorVWAP2", CurrentBar, 0, SD2Upper, SD2Lower, Brushes.Transparent, bandAreaColor, bandAreaColorOpacity);
                Draw.Region(this, "ColorVAP2", CurrentBar, 0, SD3Upper, SD3Lower, Brushes.Transparent, bandAreaColor, bandAreaColorOpacity);
            }
            else if (numStdDev == 2)
            {
                SD1Upper[0] = VWAPLine[0] + sd1Multi * SD;
                SD1Lower[0] = VWAPLine[0] - sd1Multi * SD;
                SD2Upper[0] = VWAPLine[0] + sd2Multi * SD;
                SD2Lower[0] = VWAPLine[0] - sd2Multi * SD;

                // Draw Regions
                Draw.Region(this, "ColorVWAP", CurrentBar, 0, SD1Upper, SD1Lower, Brushes.Transparent, bandAreaColor, bandAreaColorOpacity);
                Draw.Region(this, "ColorVWAP2", CurrentBar, 0, SD2Upper, SD2Lower, Brushes.Transparent, bandAreaColor, bandAreaColorOpacity);
            }
            else
            {
                SD1Upper[0] = VWAPLine[0] + sd1Multi * SD;
                SD1Lower[0] = VWAPLine[0] + sd1Multi * SD;

                // Draw Region
                Draw.Region(this, "ColorVWAP", CurrentBar, 0, SD1Upper, SD1Lower, Brushes.Transparent, bandAreaColor, bandAreaColorOpacity);
            }
        }

        private DateTime Now
        {
            get { return Bars.IsInReplayMode ? Bars.Instrument.GetMarketDataConnection().Now : DateTime.Now; }
        }
        #endregion

	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private VWAP[] cacheVWAP;
		public VWAP VWAP()
		{
			return VWAP(Input);
		}

		public VWAP VWAP(ISeries<double> input)
		{
			if (cacheVWAP != null)
				for (int idx = 0; idx < cacheVWAP.Length; idx++)
					if (cacheVWAP[idx] != null &&  cacheVWAP[idx].EqualsInput(input))
						return cacheVWAP[idx];
			return CacheIndicator<VWAP>(new VWAP(), input, ref cacheVWAP);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.VWAP VWAP()
		{
			return indicator.VWAP(Input);
		}

		public Indicators.VWAP VWAP(ISeries<double> input )
		{
			return indicator.VWAP(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.VWAP VWAP()
		{
			return indicator.VWAP(Input);
		}

		public Indicators.VWAP VWAP(ISeries<double> input )
		{
			return indicator.VWAP(input);
		}
	}
}

#endregion
