#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
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

		// Freno por aciertos: la ventana cuenta desde la última pausa.
		private int inicioVentanaFreno;
		private DateTime pausaHasta = DateTime.MinValue;

		// Registro CSV de operaciones reales y avisos.
		private static readonly CultureInfo inv = CultureInfo.InvariantCulture;
		private string rutaRegistro;
		private int tradesRegistrados;
		private double precioSenal;
		private DateTime ultimoAvisoRollover = DateTime.MinValue;

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
				MaxATRvsStop			= 2.0;

				FrenoOperaciones		= 20;
				FrenoAciertosMin		= 68;
				FrenoDiasPausa			= 5;

				RegistrarCSV			= true;
				Diagnostico				= true;
				DiasAvisoRollover		= 10;

				OperacionesHoy			= 0;
			}
			else if (State == State.Configure)
			{
				// Apagar Diagnostico acelera mucho las optimizaciones del Strategy Analyzer.
				TraceOrders = Diagnostico;
			}
			else if (State == State.DataLoaded)
			{
				EMA1				= EMA(Close, EmaRapida);
				EMA2				= EMA(Close, EmaLenta);
				if (EmaTendencia > 0)
					EMATendencia	= EMA(Close, EmaTendencia);
				ATR1				= ATR(AtrPeriodo);
				zonaNY				= TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
				rutaRegistro		= Path.Combine(Core.Globals.UserDataDir, Name + "_operaciones.csv");
				SetProfitTarget(SenalCompra, CalculationMode.Ticks, ProfitTicks);
				SetStopLoss(SenalCompra, CalculationMode.Ticks, StopTicks, false);
				SetProfitTarget(SenalVenta, CalculationMode.Ticks, ProfitTicks);
				SetStopLoss(SenalVenta, CalculationMode.Ticks, StopTicks, false);
			}
			else if (State == State.Realtime)
			{
				// Las operaciones simuladas sobre el histórico no cuentan para los límites del día.
				// Si la estrategia se reinició, las operaciones reales de hoy se leen del registro.
				int opsReales;
				double resultadoReal;
				LeerRegistroDeHoy(out opsReales, out resultadoReal);
				OperacionesHoy = opsReales;
				beneficioInicioDia = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - resultadoReal;
				bloqueoDiario = false;
				tradesRegistrados = SystemPerformance.RealTimeTrades.Count;
				if (opsReales > 0)
					Print(string.Format("Registro de hoy: {0} operaciones, resultado {1:0.00}", opsReales, resultadoReal));
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0)
				return;

			ActualizarVWAP();
			RegistrarOperacionesNuevas();

			DateTime horaNY = ANuevaYork(Time[0]);
			int hhmm = horaNY.Hour * 100 + horaNY.Minute;
			RevisarRollover(horaNY);

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
				Avisar("BloqueoDiario", string.Format("{0:yyyy-MM-dd} | Bloqueo diario | Resultado del día={1:0.00}", horaNY, beneficioHoy));
			}

			RevisarFreno(horaNY);

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
				|| horaNY.Date < pausaHasta
				|| OperacionesHoy >= MaxOperacionesDia)
				return;

			int lado = 0;
			if (PermitirLargos && CrossAbove(EMA1, EMA2, 1) && PasaFiltros(1))
				lado = 1;
			else if (PermitirCortos && CrossBelow(EMA1, EMA2, 1) && PasaFiltros(-1))
				lado = -1;
			if (lado == 0)
				return;

			int stop, objetivo;
			CalcularSalidas(out stop, out objetivo);

			// Si la vela promedio es mucho más grande que el stop, el stop queda metido en el ruido.
			double atrTicks = ATR1[0] / TickSize;
			if (MaxATRvsStop > 0 && atrTicks > MaxATRvsStop * stop)
			{
				if (Diagnostico)
					Print(string.Format("{0:yyyy-MM-dd HH:mm} | Señal descartada por volatilidad | ATR={1:0} ticks | Stop={2} ticks",
						horaNY, atrTicks, stop));
				return;
			}

			string senal = lado == 1 ? SenalCompra : SenalVenta;
			SetStopLoss(senal, CalculationMode.Ticks, stop, false);
			SetProfitTarget(senal, CalculationMode.Ticks, objetivo);
			OperacionesHoy++;
			precioSenal = Close[0];
			if (lado == 1)
				EnterLong(Contratos, senal);
			else
				EnterShort(Contratos, senal);
		}

		private DateTime ANuevaYork(DateTime tiempo)
		{
			// Hora de Nueva York, sin importar la zona horaria del PC ni el cambio de hora.
			return TimeZoneInfo.ConvertTime(
				DateTime.SpecifyKind(tiempo, DateTimeKind.Unspecified),
				Core.Globals.GeneralOptions.TimeZoneInfo,
				zonaNY);
		}

		// Imprime siempre; en tiempo real además lanza una alerta con sonido en NinjaTrader.
		private void Avisar(string id, string mensaje)
		{
			Print(mensaje);
			if (State == State.Realtime)
				Alert(id, Priority.High, mensaje, Path.Combine(Core.Globals.InstallDir, "sounds", "Alert1.wav"), 60, Brushes.DarkRed, Brushes.White);
		}

		// Pausa las entradas si los aciertos de las últimas N operaciones caen bajo el mínimo.
		private void RevisarFreno(DateTime horaNY)
		{
			if (FrenoOperaciones <= 0 || horaNY.Date < pausaHasta)
				return;
			int total = SystemPerformance.AllTrades.Count;
			if (total - inicioVentanaFreno < FrenoOperaciones)
				return;
			int ganadas = 0;
			for (int i = total - FrenoOperaciones; i < total; i++)
				if (SystemPerformance.AllTrades[i].ProfitCurrency > 0)
					ganadas++;
			double aciertos = 100.0 * ganadas / FrenoOperaciones;
			if (aciertos >= FrenoAciertosMin)
				return;
			pausaHasta = horaNY.Date.AddDays(FrenoDiasPausa);
			inicioVentanaFreno = total;
			Avisar("Freno", string.Format("{0:yyyy-MM-dd} | Freno: {1:0}% de aciertos en las últimas {2} operaciones (mínimo {3:0}%). Sin entradas hasta {4:yyyy-MM-dd}.",
				horaNY, aciertos, FrenoOperaciones, FrenoAciertosMin, pausaHasta));
		}

		private void RevisarRollover(DateTime horaNY)
		{
			if (DiasAvisoRollover <= 0 || State != State.Realtime || ultimoAvisoRollover == horaNY.Date)
				return;
			ultimoAvisoRollover = horaNY.Date;

			// "MNQ 12-26" -> diciembre de 2026. El contrato continuo ("MNQ ##-##") no aplica.
			string[] partes = Instrument.FullName.Split(' ');
			string[] mesAnio = partes[partes.Length - 1].Split('-');
			int mes, anio;
			if (mesAnio.Length != 2 || !int.TryParse(mesAnio[0], out mes) || !int.TryParse(mesAnio[1], out anio) || mes < 1 || mes > 12)
				return;

			// Los futuros de índices de CME vencen el tercer viernes del mes y se cambian 8 días antes.
			DateTime vencimiento = new DateTime(2000 + anio, mes, 1);
			while (vencimiento.DayOfWeek != DayOfWeek.Friday)
				vencimiento = vencimiento.AddDays(1);
			vencimiento = vencimiento.AddDays(14);
			DateTime rollover = vencimiento.AddDays(-8);
			int dias = (rollover - horaNY.Date).Days;
			if (dias > DiasAvisoRollover)
				return;

			string siguiente = string.Format("{0} {1:MM-yy}", partes[0], new DateTime(2000 + anio, mes, 1).AddMonths(3));
			if (dias > 0)
				Avisar("Rollover", string.Format("Faltan {0} días para el rollover de {1} ({2:yyyy-MM-dd}). Ese día pasa el gráfico a {3}.",
					dias, Instrument.FullName, rollover, siguiente));
			else
				Avisar("Rollover", string.Format("Ya pasó el rollover de {0} ({1:yyyy-MM-dd}) y el contrato pierde liquidez. Pasa el gráfico a {2}.",
					Instrument.FullName, rollover, siguiente));
		}

		// Agrega al CSV cada operación real (tiempo real) que se haya cerrado.
		private void RegistrarOperacionesNuevas()
		{
			if (State != State.Realtime)
				return;
			while (tradesRegistrados < SystemPerformance.RealTimeTrades.Count)
			{
				Trade t = SystemPerformance.RealTimeTrades[tradesRegistrados];
				tradesRegistrados++;
				if (!RegistrarCSV)
					continue;
				int lado = t.Entry.MarketPosition == MarketPosition.Long ? 1 : -1;
				double slippage = precioSenal > 0 ? lado * (t.Entry.Price - precioSenal) / TickSize : 0;
				DateTime entradaNY = ANuevaYork(t.Entry.Time);
				DateTime salidaNY = ANuevaYork(t.Exit.Time);
				string linea = string.Join(",", new string[] {
					salidaNY.ToString("yyyy-MM-dd", inv),
					Account.Name,
					Instrument.FullName,
					lado == 1 ? "Compra" : "Venta",
					t.Quantity.ToString(inv),
					entradaNY.ToString("HH:mm:ss", inv),
					precioSenal.ToString("0.00", inv),
					t.Entry.Price.ToString("0.00", inv),
					slippage.ToString("0", inv),
					salidaNY.ToString("HH:mm:ss", inv),
					t.Exit.Price.ToString("0.00", inv),
					(t.Exit.Name ?? "").Replace(",", " "),
					t.ProfitCurrency.ToString("0.00", inv),
					t.MaeTicks.ToString("0", inv),
					t.MfeTicks.ToString("0", inv) });
				try
				{
					if (!File.Exists(rutaRegistro))
						File.AppendAllText(rutaRegistro, "FechaNY,Cuenta,Instrumento,Lado,Cantidad,EntradaNY,PrecioSenal,PrecioEntrada,SlippageTicks,"
							+ "SalidaNY,PrecioSalida,Motivo,GananciaUSD,MAETicks,MFETicks" + Environment.NewLine);
					File.AppendAllText(rutaRegistro, linea + Environment.NewLine);
				}
				catch (Exception e)
				{
					Print("No se pudo escribir el registro " + rutaRegistro + ": " + e.Message);
				}
			}
		}

		// Operaciones reales de hoy (misma cuenta e instrumento) según el CSV, para no perder los límites al reiniciar.
		private void LeerRegistroDeHoy(out int operaciones, out double resultado)
		{
			operaciones = 0;
			resultado = 0;
			if (!RegistrarCSV || !File.Exists(rutaRegistro))
				return;
			string hoy = fechaContador.ToString("yyyy-MM-dd", inv);
			try
			{
				foreach (string linea in File.ReadAllLines(rutaRegistro))
				{
					string[] c = linea.Split(',');
					double ganancia;
					if (c.Length < 13 || c[0] != hoy || c[1] != Account.Name || c[2] != Instrument.FullName
						|| !double.TryParse(c[12], NumberStyles.Float, inv, out ganancia))
						continue;
					operaciones++;
					resultado += ganancia;
				}
			}
			catch (Exception e)
			{
				Print("No se pudo leer el registro " + rutaRegistro + ": " + e.Message);
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

		// Stop y objetivo de la próxima entrada (en ticks fijos o según el ATR actual).
		private void CalcularSalidas(out int stop, out int objetivo)
		{
			stop = StopTicks;
			objetivo = ProfitTicks;
			if (UsarATR)
			{
				stop = Math.Max(MinimoTicks, (int)Math.Round(StopATR * ATR1[0] / TickSize));
				objetivo = Math.Max(MinimoTicks, (int)Math.Round(ProfitATR * ATR1[0] / TickSize));
			}
		}

		protected override void OnPositionUpdate(Position position, double averagePrice, int quantity, MarketPosition marketPosition)
		{
			// Registrar apenas se cierra, para que un reinicio inmediato no pierda la operación.
			if (marketPosition == MarketPosition.Flat)
				RegistrarOperacionesNuevas();
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
			if (order == null || !Diagnostico)
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

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name="MaxATRvsStop", Description="No entrar si el ATR (en ticks) supera este múltiplo del stop. 0 = apagado", Order=5, GroupName="4. Riesgo")]
		public double MaxATRvsStop
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name="FrenoOperaciones", Description="Cuántas operaciones recientes se miran. 0 = freno apagado", Order=1, GroupName="5. Freno por aciertos")]
		public int FrenoOperaciones
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name="FrenoAciertosMin", Description="Si los aciertos de esas operaciones caen bajo este %, se pausa", Order=2, GroupName="5. Freno por aciertos")]
		public double FrenoAciertosMin
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="FrenoDiasPausa", Description="Días de pausa cuando salta el freno", Order=3, GroupName="5. Freno por aciertos")]
		public int FrenoDiasPausa
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="RegistrarCSV", Description="Guardar cada operación real en Documentos\\NinjaTrader 8\\MNQAutomaticoPrueba_operaciones.csv", Order=1, GroupName="6. Registro y avisos")]
		public bool RegistrarCSV
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="Diagnostico", Description="Mostrar el detalle de las órdenes. Apagarlo para optimizar", Order=2, GroupName="6. Registro y avisos")]
		public bool Diagnostico
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name="DiasAvisoRollover", Description="Avisar cuando falten estos días para cambiar de contrato. 0 = apagado", Order=3, GroupName="6. Registro y avisos")]
		public int DiasAvisoRollover
		{ get; set; }
		#endregion

	}
}
