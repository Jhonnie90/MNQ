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
		private const string SenalCompra = "CompraEMA";
		private const string SenalVenta = "VentaEMA";

		private int OperacionesHoy;
		private DateTime fechaContador = DateTime.MinValue;
		private double beneficioInicioDia;
		private bool bloqueoDiario;
		private EMA EMA1;
		private EMA EMA2;
		private EMA EMATendencia;
		private ATR ATR1;
		private TimeZoneInfo zonaNY;

		// VWAP de la sesión, acumulado vela a vela.
		private double sumaPrecioVolumen;
		private double sumaVolumen;
		private double vwap;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Cruce de EMAs en MNQ después de la apertura de Nueva York, con filtro VWAP y salidas protegidas. Pensada para gráfico de 5 minutos.";
				Name										= "MNQAutomaticoPrueba";
				Calculate									= Calculate.OnBarClose;
				IncludeCommission							= true;
				IncludeTradeHistoryInBacktest				= true;
				EntriesPerDirection							= 1;
				EntryHandling								= EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy				= true;
				ExitOnSessionCloseSeconds					= 30;
				IsFillLimitOnTouch							= false;
				MaximumBarsLookBack							= MaximumBarsLookBack.TwoHundredFiftySix;
				// Con stops de pocos ticks el modo Standard adivina qué se tocó primero dentro
				// de la vela. High revisa las salidas en velas de 1 minuto.
				OrderFillResolution							= OrderFillResolution.High;
				OrderFillResolutionType						= BarsPeriodType.Minute;
				OrderFillResolutionValue					= 1;
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

				EmaRapida				= 9;
				EmaLenta				= 21;
				PermitirLargos			= true;
				PermitirCortos			= false;
				FiltroVWAP				= true;
				EmaTendencia			= 0;

				UsarATR					= false;
				StopTicks				= 120;
				ProfitTicks				= 60;
				AtrPeriodo				= 14;
				StopATR					= 2.0;
				ProfitATR				= 1.0;
				MinimoTicks				= 8;

				HoraInicio				= 935;
				HoraFin					= 1130;

				Contratos				= 1;
				MaxOperacionesDia		= 3;
				LimitePerdidaDiaUSD		= 120;
				LimiteGananciaDiaUSD	= 0;

				OperacionesHoy			= 0;
			}
			else if (State == State.Configure)
			{
			}
			else if (State == State.DataLoaded)
			{
				EMA1				= EMA(Close, EmaRapida);
				EMA2				= EMA(Close, EmaLenta);
				if (EmaTendencia > 0)
					EMATendencia	= EMA(Close, EmaTendencia);
				ATR1				= ATR(AtrPeriodo);
				zonaNY				= TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
				SetProfitTarget(SenalCompra, CalculationMode.Ticks, ProfitTicks);
				SetStopLoss(SenalCompra, CalculationMode.Ticks, StopTicks, false);
				SetProfitTarget(SenalVenta, CalculationMode.Ticks, ProfitTicks);
				SetStopLoss(SenalVenta, CalculationMode.Ticks, StopTicks, false);
			}
			else if (State == State.Realtime)
			{
				// Las operaciones simuladas sobre el histórico no cuentan para los límites del día.
				OperacionesHoy = 0;
				beneficioInicioDia = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
				bloqueoDiario = false;
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0)
				return;

			ActualizarVWAP();

			// Horario de Nueva York, sin importar la zona horaria del PC ni el cambio de hora.
			DateTime horaNY = TimeZoneInfo.ConvertTime(
				DateTime.SpecifyKind(Time[0], DateTimeKind.Unspecified),
				Core.Globals.GeneralOptions.TimeZoneInfo,
				zonaNY);
			int hhmm = horaNY.Hour * 100 + horaNY.Minute;

			double beneficioAcumulado =
				SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;

			// Reiniciar contadores al cambiar el día en Nueva York.
			if (fechaContador != horaNY.Date)
			{
				fechaContador = horaNY.Date;
				OperacionesHoy = 0;
				beneficioInicioDia = beneficioAcumulado;
				bloqueoDiario = false;
			}

			double beneficioHoy = beneficioAcumulado - beneficioInicioDia;

			if (!bloqueoDiario
				&& ((LimiteGananciaDiaUSD > 0 && beneficioHoy >= LimiteGananciaDiaUSD)
					|| (LimitePerdidaDiaUSD > 0 && beneficioHoy <= -LimitePerdidaDiaUSD)))
			{
				bloqueoDiario = true;
				Print(string.Format("{0:yyyy-MM-dd} | Bloqueo diario | Resultado del día={1:0.00}", horaNY, beneficioHoy));
			}

			if (CurrentBar < BarsRequiredToTrade)
				return;

			// Cerrar la posición al finalizar el horario.
			if (hhmm >= HoraFin)
			{
				if (Position.MarketPosition == MarketPosition.Long)
				{
					ExitLong(Position.Quantity, "CierreHorario", SenalCompra);
					return;
				}
				if (Position.MarketPosition == MarketPosition.Short)
				{
					ExitShort(Position.Quantity, "CierreHorario", SenalVenta);
					return;
				}
			}

			if (Position.MarketPosition != MarketPosition.Flat
				|| hhmm < HoraInicio
				|| hhmm >= HoraFin
				|| bloqueoDiario
				|| OperacionesHoy >= MaxOperacionesDia)
				return;

			if (PermitirLargos && CrossAbove(EMA1, EMA2, 1) && PasaFiltros(1))
			{
				PrepararSalidas(SenalCompra);
				OperacionesHoy++;
				EnterLong(Contratos, SenalCompra);
			}
			else if (PermitirCortos && CrossBelow(EMA1, EMA2, 1) && PasaFiltros(-1))
			{
				PrepararSalidas(SenalVenta);
				OperacionesHoy++;
				EnterShort(Contratos, SenalVenta);
			}
		}

		private void ActualizarVWAP()
		{
			if (Bars.IsFirstBarOfSession)
			{
				sumaPrecioVolumen = 0;
				sumaVolumen = 0;
			}
			double precioTipico = (High[0] + Low[0] + Close[0]) / 3.0;
			double volumen = Math.Max(Volume[0], 1.0);
			sumaPrecioVolumen += precioTipico * volumen;
			sumaVolumen += volumen;
			vwap = sumaPrecioVolumen / sumaVolumen;
		}

		// lado = 1 para compras, -1 para ventas.
		private bool PasaFiltros(int lado)
		{
			if (FiltroVWAP && (Close[0] - vwap) * lado <= 0)
				return false;
			if (EMATendencia != null && (Close[0] - EMATendencia[0]) * lado <= 0)
				return false;
			return true;
		}

		// Fija stop y objetivo antes de cada entrada (en ticks fijos o según el ATR actual).
		private void PrepararSalidas(string senal)
		{
			int stop = StopTicks;
			int objetivo = ProfitTicks;
			if (UsarATR)
			{
				stop = Math.Max(MinimoTicks, (int)Math.Round(StopATR * ATR1[0] / TickSize));
				objetivo = Math.Max(MinimoTicks, (int)Math.Round(ProfitATR * ATR1[0] / TickSize));
			}
			SetStopLoss(senal, CalculationMode.Ticks, stop, false);
			SetProfitTarget(senal, CalculationMode.Ticks, objetivo);
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
		[Display(Name="EmaRapida", Description="Periodo de la EMA rápida", Order=1, GroupName="1. Entradas")]
		public int EmaRapida
		{ get; set; }

		[NinjaScriptProperty]
		[Range(2, int.MaxValue)]
		[Display(Name="EmaLenta", Description="Periodo de la EMA lenta", Order=2, GroupName="1. Entradas")]
		public int EmaLenta
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="PermitirLargos", Description="Comprar cuando la EMA rápida cruza hacia arriba", Order=3, GroupName="1. Entradas")]
		public bool PermitirLargos
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="PermitirCortos", Description="Vender cuando la EMA rápida cruza hacia abajo", Order=4, GroupName="1. Entradas")]
		public bool PermitirCortos
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="FiltroVWAP", Description="Comprar solo sobre el VWAP de la sesión (vender solo debajo)", Order=5, GroupName="1. Entradas")]
		public bool FiltroVWAP
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name="EmaTendencia", Description="Comprar solo sobre esta EMA (vender solo debajo). 0 = apagado", Order=6, GroupName="1. Entradas")]
		public int EmaTendencia
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="UsarATR", Description="Stop y objetivo como múltiplos del ATR en vez de ticks fijos", Order=1, GroupName="2. Salidas")]
		public bool UsarATR
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="StopTicks", Description="Stop de pérdida en ticks (si UsarATR está apagado)", Order=2, GroupName="2. Salidas")]
		public int StopTicks
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="ProfitTicks", Description="Objetivo de ganancia en ticks (si UsarATR está apagado)", Order=3, GroupName="2. Salidas")]
		public int ProfitTicks
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="AtrPeriodo", Description="Periodo del ATR", Order=4, GroupName="2. Salidas")]
		public int AtrPeriodo
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0.1, double.MaxValue)]
		[Display(Name="StopATR", Description="Stop = este múltiplo del ATR", Order=5, GroupName="2. Salidas")]
		public double StopATR
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0.1, double.MaxValue)]
		[Display(Name="ProfitATR", Description="Objetivo = este múltiplo del ATR", Order=6, GroupName="2. Salidas")]
		public double ProfitATR
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="MinimoTicks", Description="Mínimo de ticks para stop y objetivo con ATR", Order=7, GroupName="2. Salidas")]
		public int MinimoTicks
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, 2359)]
		[Display(Name="HoraInicio", Description="Hora de Nueva York (HHMM) desde la que se permiten entradas", Order=1, GroupName="3. Horario (Nueva York)")]
		public int HoraInicio
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, 2359)]
		[Display(Name="HoraFin", Description="Hora de Nueva York (HHMM) en la que se cierra todo", Order=2, GroupName="3. Horario (Nueva York)")]
		public int HoraFin
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Contratos", Description="Contratos por operación", Order=1, GroupName="4. Riesgo")]
		public int Contratos
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="MaxOperacionesDia", Description="Máximo de entradas por día", Order=2, GroupName="4. Riesgo")]
		public int MaxOperacionesDia
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name="LimitePerdidaDiaUSD", Description="Deja de operar si el día pierde esto. 0 = sin límite", Order=3, GroupName="4. Riesgo")]
		public double LimitePerdidaDiaUSD
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name="LimiteGananciaDiaUSD", Description="Deja de operar si el día gana esto. 0 = sin límite", Order=4, GroupName="4. Riesgo")]
		public double LimiteGananciaDiaUSD
		{ get; set; }
		#endregion

	}
}
