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
	public class MNQAutomaticoPrueba : Strategy
	{
		private int OperacionesHoy;
        private DateTime fechaContador = DateTime.MinValue;
		private double beneficioInicioDia;
        private bool bloqueoDiario;
		private EMA EMA1;
		private EMA EMA2;
		
		private Series<int> SerieContador;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Estrategia experimental para simulación, un contrato y salidas protegidas";
				Name										= "MNQAutomaticoPrueba";
				Calculate									= Calculate.OnBarClose;
				IncludeCommission = true;
                IncludeTradeHistoryInBacktest = true;
				EntriesPerDirection							= 1;
				EntryHandling								= EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy				= true;
				ExitOnSessionCloseSeconds					= 30;
				IsFillLimitOnTouch							= false;
				MaximumBarsLookBack							= MaximumBarsLookBack.TwoHundredFiftySix;
				OrderFillResolution							= OrderFillResolution.Standard;
				Slippage									= 1;
				StartBehavior								= StartBehavior.WaitUntilFlat;
				TimeInForce									= TimeInForce.Gtc;
				TraceOrders									= true;
				RealtimeErrorHandling						= RealtimeErrorHandling.StopCancelClose;
				StopTargetHandling							= StopTargetHandling.PerEntryExecution;
				BarsRequiredToTrade							= 50;
				// Disable this property for performance gains in Strategy Analyzer optimizations
				// See the Help Guide for additional information
				IsInstantiatedOnEachOptimizationIteration	= true;
				ProfitTicks					= 24;
				StopTicks					= 16;
				OperacionesHoy					= 0;
			}
			else if (State == State.Configure)
			{
			}
			else if (State == State.DataLoaded)
			{				
				SerieContador = new Series<int>(this);			
				EMA1				= EMA(Close, 9);
				EMA2				= EMA(Close, 21);
				SetProfitTarget("CompraEMA", CalculationMode.Ticks, ProfitTicks);
				SetStopLoss("CompraEMA", CalculationMode.Ticks, StopTicks, false);
			}
		}

	protected override void OnBarUpdate()
{
    if (BarsInProgress != 0)
        return;

    // Reiniciar el contador al cambiar la fecha del gráfico.
       double beneficioAcumulado =
        SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;

    if (fechaContador != Time[0].Date)
    {
        fechaContador = Time[0].Date;
        OperacionesHoy = 0;
        beneficioInicioDia = beneficioAcumulado;
        bloqueoDiario = false;
    }

    double beneficioHoy = beneficioAcumulado - beneficioInicioDia;

    if (beneficioHoy >= 20.0 || beneficioHoy <= -20.0)
        bloqueoDiario = true;

    if (CurrentBar < BarsRequiredToTrade)
        return;

    TimeSpan hora = Time[0].TimeOfDay;

    // Cerrar la compra al finalizar el horario.
    if (Position.MarketPosition == MarketPosition.Long
        && hora >= new TimeSpan(9, 0, 0))
    {
        ExitLong(1, "CierreHorario", "CompraEMA");
        return;
    }

    // Máximo tres intentos de entrada por fecha.
    if (Position.MarketPosition == MarketPosition.Flat
        && hora >= new TimeSpan(7, 30, 0)
        && hora < new TimeSpan(9, 0, 0)
        && !bloqueoDiario
		&& OperacionesHoy < 3
        && CrossAbove(EMA1, EMA2, 1))
    {
        OperacionesHoy++;
        EnterLong(1, "CompraEMA");
    }
}
protected override void OnOrderUpdate(
    Order order,
    double limitPrice,
    double stopPrice,
    int quantity,
    int filled,
    double averageFillPrice,
    OrderState orderState,
    DateTime time,
    ErrorCode error,
    string comment)
{
    if (order == null)
        return;

    Print(string.Format(
        "{0:yyyy-MM-dd HH:mm:ss} | {1} | {2} | Estado={3}"
        + " | Cantidad={4} | Ejecutados={5} | Error={6} | {7}",
        time,
        order.Name,
        order.OrderAction,
        orderState,
        quantity,
        filled,
        error,
        comment));
}
		#region Properties
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="ProfitTicks", Description="Objetivo de ganancia en ticks", Order=1, GroupName="Parameters")]
		public int ProfitTicks
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="StopTicks", Description="Stop de pérdida en ticks", Order=2, GroupName="Parameters")]
		public int StopTicks
		{ get; set; }
		#endregion

	}
}
