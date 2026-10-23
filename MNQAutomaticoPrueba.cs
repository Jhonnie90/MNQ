#region Using declarations
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
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

		// Freno por aciertos: mientras está activo, las señales se siguen en simulación ("sombra").
		private FrenoAciertos freno;
		private int tradesContadosFreno;
		private OperacionSombra sombraActiva;
		private int operacionesSombraHoy;

		// Apertura de las 9:30 y cierre de las 16:00 de Nueva York, para los filtros de gap y apertura.
		private readonly SesionRTH sesion = new SesionRTH();

		// Registro CSV de operaciones reales, validación en vivo y avisos.
		private static readonly CultureInfo inv = CultureInfo.InvariantCulture;
		private string rutaRegistro;
		private int tradesRegistrados;
		private double precioSenal;
		private string ultimoEstadoValidacion;
		private DateTime ultimoAvisoRollover = DateTime.MinValue;
		private bool rolloverPasado;
		private bool avisoDescuadre;
		private bool resumenEnviado;
		private bool estuvoEnVivo;
		private int barrasNoPlano;
		private bool avisoNoPlano;
		private volatile string errorTelegram;

		// Noticias: horas exactas (Nueva York) y días completos sin operar.
		private readonly List<DateTime> noticias = new List<DateTime>();
		private readonly HashSet<DateTime> diasNoticia = new HashSet<DateTime>();

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
				// Un microcorte de menos de 30 s no detiene la estrategia; los cambios de conexión se avisan.
				ConnectionLossHandling						= ConnectionLossHandling.Recalculate;
				DisconnectDelaySeconds						= 30;
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
				FiltroGap				= false;
				FiltroSobreApertura		= false;
				FiltroPrimeraVela		= false;
				FiltroSobreCierreAnterior = false;

				UsarATR					= false;
				StopTicks				= 120;
				ProfitTicks				= 60;
				AtrPeriodo				= 14;
				StopATR					= 2.0;
				ProfitATR				= 1.0;
				MinimoTicks				= 8;
				SalidaTiempoVelas		= 0;
				SalidaTiempoMinTicks	= 0;

				HoraInicio				= 935;
				HoraFin					= 1130;

				Contratos				= 1;
				MaxOperacionesDia		= 3;
				LimitePerdidaDiaUSD		= 120;
				LimiteGananciaDiaUSD	= 0;
				MaxATRvsStop			= 2.0;
				MaxRiesgoOperacionUSD	= 0;

				FrenoOperaciones		= 50;
				FrenoAciertosMin		= 64;
				FrenoAciertosReanudar	= 68;
				ValidacionMinimo		= 68;
				ValidacionEsperado		= 79;

				BloquearTrasRollover	= true;
				CerrarPosicionHuerfana	= false;
				MinutosNoticias			= 15;

				RegistrarCSV			= true;
				Diagnostico				= true;
				DiasAvisoRollover		= 10;
				TelegramToken			= string.Empty;
				TelegramChatId			= string.Empty;
				UrlLatido				= string.Empty;

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
				freno				= new FrenoAciertos(FrenoOperaciones, FrenoAciertosMin, FrenoAciertosReanudar);
				CargarNoticias();
				SetProfitTarget(SenalCompra, CalculationMode.Ticks, ProfitTicks);
				SetStopLoss(SenalCompra, CalculationMode.Ticks, StopTicks, false);
				SetProfitTarget(SenalVenta, CalculationMode.Ticks, ProfitTicks);
				SetStopLoss(SenalVenta, CalculationMode.Ticks, StopTicks, false);
			}
			else if (State == State.Realtime)
			{
				// Las operaciones simuladas sobre el histórico no cuentan para los límites del día.
				// Si la estrategia se reinició, las operaciones reales de hoy se leen del registro.
				estuvoEnVivo = true;
				int opsReales;
				double resultadoReal;
				LeerRegistro(true, out opsReales, out resultadoReal, null);
				OperacionesHoy = opsReales;
				beneficioInicioDia = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - resultadoReal;
				bloqueoDiario = false;
				resumenEnviado = false;
				tradesRegistrados = SystemPerformance.RealTimeTrades.Count;
				if (opsReales > 0)
					Print(string.Format("Registro de hoy: {0} operaciones, resultado {1:0.00}", opsReales, resultadoReal));
				EvaluarValidacion();
				if (freno.Pausado)
					Avisar("Freno", string.Format("Freno activo al arrancar: {0:0}% de aciertos en las últimas {1} operaciones. Las señales se siguen en simulación.",
						freno.AciertosVentana, FrenoOperaciones));
				EnviarTelegram("Estrategia activa en " + Account.Name + " con " + Instrument.FullName);
			}
			else if (State == State.Terminated)
			{
				// También se dispara al cerrar NinjaTrader: un aviso al celular de que el bot dejó de correr.
				if (estuvoEnVivo)
					EnviarTelegram("Estrategia detenida", true);
			}
		}

		protected override void OnConnectionStatusUpdate(ConnectionStatusEventArgs connectionStatusUpdate)
		{
			if (State == State.Realtime)
				Avisar("Conexion", string.Format("Conexión: órdenes {0}, precios {1}", connectionStatusUpdate.Status, connectionStatusUpdate.PriceStatus));
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0)
				return;

			ActualizarVWAP();
			RegistrarOperacionesNuevas();
			if (errorTelegram != null)
			{
				Print("No se pudo enviar a Telegram: " + errorTelegram);
				errorTelegram = null;
			}

			DateTime horaNY = ANuevaYork(Time[0]);
			int hhmm = horaNY.Hour * 100 + horaNY.Minute;
			sesion.Actualizar(horaNY, Open[0], Close[0], CurrentBar > 0 ? Close[1] : Close[0]);
			RevisarRollover(horaNY);

			double beneficioAcumulado =
				SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;

			// Reiniciar contadores al cambiar el día en Nueva York.
			if (fechaContador != horaNY.Date)
			{
				fechaContador = horaNY.Date;
				OperacionesHoy = 0;
				operacionesSombraHoy = 0;
				beneficioInicioDia = beneficioAcumulado;
				bloqueoDiario = false;
				resumenEnviado = false;
				barrasNoPlano = 0;
				avisoNoPlano = false;
			}

			double beneficioHoy = beneficioAcumulado - beneficioInicioDia;

			if (!bloqueoDiario
				&& ((LimiteGananciaDiaUSD > 0 && beneficioHoy >= LimiteGananciaDiaUSD)
					|| (LimitePerdidaDiaUSD > 0 && beneficioHoy <= -LimitePerdidaDiaUSD)))
			{
				bloqueoDiario = true;
				Avisar("BloqueoDiario", string.Format("{0:yyyy-MM-dd} | Bloqueo diario | Resultado del día={1:0.00}", horaNY, beneficioHoy));
			}

			ActualizarSombra(horaNY, hhmm);
			RevisarFreno(horaNY);
			EnviarLatido(horaNY, hhmm, beneficioHoy);

			if (CurrentBar < BarsRequiredToTrade)
				return;

			// Cerrar la posición al finalizar el horario.
			if (hhmm >= HoraFin)
			{
				// Si dos velas después del cierre la estrategia sigue en posición, algo falló (orden rechazada, desconexión).
				if (Position.MarketPosition != MarketPosition.Flat && ++barrasNoPlano >= 3 && !avisoNoPlano)
				{
					avisoNoPlano = true;
					Avisar("NoPlano", string.Format("{0:HH:mm} NY | La estrategia sigue en posición después de la hora de cierre. Revisar la cuenta.", horaNY));
				}
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
				EnviarResumenDelDia(horaNY, beneficioHoy);
			}

			// Salida por tiempo (opcional): tras N velas, cerrar si no va al menos X ticks a favor.
			if (SalidaTiempoVelas > 0 && Position.MarketPosition != MarketPosition.Flat
				&& BarsSinceEntryExecution() + 1 >= SalidaTiempoVelas)
			{
				int ladoPosicion = Position.MarketPosition == MarketPosition.Long ? 1 : -1;
				if ((Close[0] - Position.AveragePrice) * ladoPosicion / TickSize < SalidaTiempoMinTicks)
				{
					if (ladoPosicion == 1)
						ExitLong(Position.Quantity, "SalidaTiempo", SenalCompra);
					else
						ExitShort(Position.Quantity, "SalidaTiempo", SenalVenta);
					return;
				}
			}

			if (!PosicionCuadra())
				return;

			if (Position.MarketPosition != MarketPosition.Flat
				|| hhmm < HoraInicio
				|| hhmm >= HoraFin
				|| (BloquearTrasRollover && rolloverPasado))
				return;

			// Con el freno activo las señales no se operan: se siguen en simulación para saber cuándo reanudar.
			bool enSombra = freno.Pausado;
			if (enSombra ? (sombraActiva != null || operacionesSombraHoy >= MaxOperacionesDia)
				: (bloqueoDiario || OperacionesHoy >= MaxOperacionesDia))
				return;

			int lado = 0;
			if (PermitirLargos && CrossAbove(EMA1, EMA2, 1) && PasaFiltros(1))
				lado = 1;
			else if (PermitirCortos && CrossBelow(EMA1, EMA2, 1) && PasaFiltros(-1))
				lado = -1;
			if (lado == 0)
				return;

			if (HayNoticiaCerca(horaNY))
			{
				if (Diagnostico)
					Print(string.Format("{0:yyyy-MM-dd HH:mm} | Señal descartada por noticia cercana", horaNY));
				return;
			}

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

			if (enSombra)
			{
				sombraActiva = new OperacionSombra(lado, stop * TickSize, objetivo * TickSize, Slippage * TickSize, TickSize);
				operacionesSombraHoy++;
				if (Diagnostico)
					Print(string.Format("{0:yyyy-MM-dd HH:mm} | Señal en simulación (freno activo)", horaNY));
				return;
			}

			double riesgo = stop * TickSize * Instrument.MasterInstrument.PointValue * Contratos;
			if (MaxRiesgoOperacionUSD > 0 && riesgo > MaxRiesgoOperacionUSD)
			{
				Print(string.Format("{0:yyyy-MM-dd HH:mm} | Señal descartada: arriesga {1:0.00} USD y el máximo es {2:0.00}",
					horaNY, riesgo, MaxRiesgoOperacionUSD));
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

		// Imprime siempre; en tiempo real además lanza una alerta con sonido en NinjaTrader y la manda a Telegram.
		private void Avisar(string id, string mensaje)
		{
			Print(mensaje);
			if (State != State.Realtime)
				return;
			Alert(id, Priority.High, mensaje, Path.Combine(Core.Globals.InstallDir, "sounds", "Alert1.wav"), 60, Brushes.DarkRed, Brushes.White);
			EnviarTelegram(mensaje);
		}

		// Envío en segundo plano para no frenar la estrategia; el error se muestra en la vela siguiente.
		// "forzar" permite avisar fuera de tiempo real (al detenerse la estrategia).
		private void EnviarTelegram(string mensaje, bool forzar = false)
		{
			if ((!forzar && State != State.Realtime) || string.IsNullOrEmpty(TelegramToken) || string.IsNullOrEmpty(TelegramChatId))
				return;
			string url = "https://api.telegram.org/bot" + TelegramToken.Trim() + "/sendMessage";
			NameValueCollection datos = new NameValueCollection();
			datos["chat_id"] = TelegramChatId.Trim();
			datos["text"] = Name + " | " + mensaje;
			Task.Run(() =>
			{
				try
				{
					ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
					using (WebClient cliente = new WebClient())
						cliente.UploadValues(url, datos);
				}
				catch (Exception e)
				{
					errorTelegram = e.Message;
				}
			});
		}

		// Latido para un vigilante externo (p. ej. healthchecks.io o un servidor propio): en cada vela,
		// desde 30 min antes de la ventana hasta 15 min después, se envía el estado. Si deja de llegar,
		// el vigilante avisa que el bot está caído, cosa que el bot no puede avisar por sí mismo.
		private void EnviarLatido(DateTime horaNY, int hhmm, double beneficioHoy)
		{
			if (State != State.Realtime || string.IsNullOrEmpty(UrlLatido))
				return;
			int minutos = hhmm / 100 * 60 + hhmm % 100;
			if (minutos < HoraInicio / 100 * 60 + HoraInicio % 100 - 30 || minutos > HoraFin / 100 * 60 + HoraFin % 100 + 15)
				return;
			string estado = string.Format(inv, "{0:yyyy-MM-dd HH:mm} NY | {1} | {2} | posicion={3} {4} | hoy={5:0.00} | ops={6} | freno={7} | bloqueo={8}",
				horaNY, Account.Name, Instrument.FullName, Position.MarketPosition, Position.Quantity, beneficioHoy, OperacionesHoy,
				freno.Pausado ? "activo" : "no", bloqueoDiario ? "si" : "no");
			string url = UrlLatido.Trim();
			Task.Run(() =>
			{
				try
				{
					ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
					using (WebClient cliente = new WebClient())
						cliente.UploadString(url, estado);
				}
				catch (Exception e)
				{
					errorTelegram = "latido: " + e.Message;
				}
			});
		}

		private void EnviarResumenDelDia(DateTime horaNY, double beneficioHoy)
		{
			if (resumenEnviado || State != State.Realtime)
				return;
			resumenEnviado = true;
			string mensaje = string.Format("{0:yyyy-MM-dd} | Resumen del día: {1} operaciones, resultado {2:0.00} USD{3}",
				horaNY, OperacionesHoy, beneficioHoy, freno.Pausado ? " | freno activo, operando en simulación" : "");
			Print(mensaje);
			EnviarTelegram(mensaje);
		}

		// Suma al freno cada operación real cerrada (en backtest, todas las simuladas).
		private void RevisarFreno(DateTime horaNY)
		{
			while (tradesContadosFreno < SystemPerformance.AllTrades.Count)
			{
				bool gano = SystemPerformance.AllTrades[tradesContadosFreno].ProfitCurrency > 0;
				tradesContadosFreno++;
				InformarFreno(freno.Agregar(gano), horaNY);
			}
		}

		// Avanza la operación en simulación abierta mientras el freno está activo.
		private void ActualizarSombra(DateTime horaNY, int hhmm)
		{
			if (sombraActiva == null)
				return;
			int resultado = sombraActiva.Actualizar(Open[0], High[0], Low[0], Close[0], hhmm >= HoraFin);
			if (resultado == 0)
				return;
			sombraActiva = null;
			if (Diagnostico)
				Print(string.Format("{0:yyyy-MM-dd HH:mm} | Operación en simulación: {1}", horaNY, resultado > 0 ? "ganó" : "perdió"));
			InformarFreno(freno.Agregar(resultado > 0), horaNY);
		}

		private void InformarFreno(int cambio, DateTime horaNY)
		{
			if (cambio > 0)
				Avisar("Freno", string.Format("{0:yyyy-MM-dd} | Freno: {1:0}% de aciertos en las últimas {2} operaciones (mínimo {3:0}%). "
					+ "Sin entradas reales; las señales se siguen en simulación hasta volver a {4:0}%.",
					horaNY, freno.AciertosVentana, FrenoOperaciones, FrenoAciertosMin, FrenoAciertosReanudar));
			else if (cambio < 0)
				Avisar("Freno", string.Format("{0:yyyy-MM-dd} | Freno liberado: {1:0}% de aciertos en las últimas {2} operaciones. Se reanudan las entradas reales.",
					horaNY, freno.AciertosVentana, FrenoOperaciones));
		}

		// En tiempo real, compara la posición de la estrategia con la de la cuenta. Si no cuadran
		// (p. ej. se reactivó la estrategia con una posición abierta), no entra para no apilar contratos.
		private bool PosicionCuadra()
		{
			if (State != State.Realtime || Account == null)
				return true;
			int enCuenta = 0;
			lock (Account.Positions)
				foreach (Position p in Account.Positions)
					if (p.Instrument == Instrument && p.MarketPosition != MarketPosition.Flat)
						enCuenta = p.MarketPosition == MarketPosition.Long ? p.Quantity : -p.Quantity;
			int enEstrategia = Position.MarketPosition == MarketPosition.Long ? Position.Quantity
				: Position.MarketPosition == MarketPosition.Short ? -Position.Quantity : 0;
			if (enCuenta == enEstrategia)
			{
				avisoDescuadre = false;
				return true;
			}
			if (!avisoDescuadre)
			{
				avisoDescuadre = true;
				Avisar("Descuadre", string.Format("La cuenta {0} tiene {1} contratos de {2} y la estrategia {3}. Sin entradas nuevas hasta que cuadren.",
					Account.Name, enCuenta, Instrument.FullName, enEstrategia));
				if (CerrarPosicionHuerfana && enEstrategia == 0)
				{
					Account.Flatten(new List<Instrument> { Instrument });
					Avisar("Descuadre", "Se cerró a mercado la posición de la cuenta que la estrategia no manejaba.");
				}
			}
			return false;
		}

		// Lee Documentos\NinjaTrader 8\<estrategia>_noticias.csv: "yyyy-MM-dd,HH:mm,descripción" en hora de
		// Nueva York. Sin hora = todo el día sin operar. Las líneas que empiezan con # se ignoran.
		private void CargarNoticias()
		{
			noticias.Clear();
			diasNoticia.Clear();
			string ruta = Path.Combine(Core.Globals.UserDataDir, Name + "_noticias.csv");
			if (MinutosNoticias <= 0 || !File.Exists(ruta))
				return;
			try
			{
				foreach (string linea in File.ReadAllLines(ruta))
				{
					string[] c = linea.Trim().Split(',');
					DateTime dia, hora;
					if (c[0].Length == 0 || c[0].StartsWith("#")
						|| !DateTime.TryParseExact(c[0].Trim(), "yyyy-MM-dd", inv, DateTimeStyles.None, out dia))
						continue;
					if (c.Length > 1 && DateTime.TryParseExact(c[1].Trim(), "HH:mm", inv, DateTimeStyles.None, out hora))
						noticias.Add(dia.Add(hora.TimeOfDay));
					else
						diasNoticia.Add(dia);
				}
				Print(string.Format("Noticias cargadas: {0} horas y {1} días completos ({2})", noticias.Count, diasNoticia.Count, ruta));
			}
			catch (Exception e)
			{
				Print("No se pudo leer " + ruta + ": " + e.Message);
			}
		}

		private bool HayNoticiaCerca(DateTime horaNY)
		{
			if (MinutosNoticias <= 0)
				return false;
			if (diasNoticia.Contains(horaNY.Date))
				return true;
			foreach (DateTime n in noticias)
				if (Math.Abs((horaNY - n).TotalMinutes) <= MinutosNoticias)
					return true;
			return false;
		}

		private void RevisarRollover(DateTime horaNY)
		{
			if (State != State.Realtime || ultimoAvisoRollover == horaNY.Date)
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
			rolloverPasado = dias <= 0;
			if (DiasAvisoRollover <= 0 || dias > DiasAvisoRollover)
				return;

			string siguiente = string.Format("{0} {1:MM-yy}", partes[0], new DateTime(2000 + anio, mes, 1).AddMonths(3));
			if (dias > 0)
				Avisar("Rollover", string.Format("Faltan {0} días para el rollover de {1} ({2:yyyy-MM-dd}). Ese día pasa el gráfico a {3}.",
					dias, Instrument.FullName, rollover, siguiente));
			else
				Avisar("Rollover", string.Format("Ya pasó el rollover de {0} ({1:yyyy-MM-dd}) y el contrato pierde liquidez. Pasa el gráfico a {2}.{3}",
					Instrument.FullName, rollover, siguiente, BloquearTrasRollover ? " Sin entradas nuevas en este contrato." : ""));
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
				int lado = t.Entry.MarketPosition == MarketPosition.Long ? 1 : -1;
				double slippage = precioSenal > 0 ? lado * (t.Entry.Price - precioSenal) / TickSize : 0;
				DateTime entradaNY = ANuevaYork(t.Entry.Time);
				DateTime salidaNY = ANuevaYork(t.Exit.Time);
				EnviarTelegram(string.Format(inv, "{0} {1:HH:mm}-{2:HH:mm} NY | {3} | {4:0.00} USD | slippage de entrada {5:0} ticks",
					lado == 1 ? "Compra" : "Venta", entradaNY, salidaNY, t.Exit.Name, t.ProfitCurrency, slippage));
				if (!RegistrarCSV)
					continue;
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
				EvaluarValidacion();
			}
		}

		// Operaciones reales (misma cuenta e instrumento) según el CSV: solo las de hoy, para no perder los
		// límites al reiniciar, o todas, para la validación en vivo. "resultados" puede ser null.
		private void LeerRegistro(bool soloHoy, out int operaciones, out double resultado, List<bool> resultados)
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
					if (c.Length < 13 || (soloHoy && c[0] != hoy) || c[1] != Account.Name || c[2] != Instrument.FullName
						|| !double.TryParse(c[12], NumberStyles.Float, inv, out ganancia))
						continue;
					operaciones++;
					resultado += ganancia;
					if (resultados != null)
						resultados.Add(ganancia > 0);
				}
			}
			catch (Exception e)
			{
				Print("No se pudo leer el registro " + rutaRegistro + ": " + e.Message);
			}
		}

		// Validación estadística de las operaciones reales: ¿los aciertos se parecen más al esperado o al punto de equilibrio?
		private void EvaluarValidacion()
		{
			if (!RegistrarCSV || ValidacionMinimo >= ValidacionEsperado)
				return;
			List<bool> resultados = new List<bool>();
			int ops;
			double resultado;
			LeerRegistro(false, out ops, out resultado, resultados);
			if (ops == 0)
				return;
			int ganadas = resultados.Count(r => r);
			int decididaEn;
			string estado = Validacion.Sprt(resultados, ValidacionMinimo / 100.0, ValidacionEsperado / 100.0, out decididaEn);
			double bajo, alto;
			Validacion.Wilson(ganadas, ops, out bajo, out alto);
			string mensaje = string.Format("Validación en vivo ({0}): {1} de {2} aciertos ({3:0.0}%), rango probable {4:0}-{5:0}% | {6}{7}",
				Account.Name, ganadas, ops, 100.0 * ganadas / ops, 100 * bajo, 100 * alto, estado,
				decididaEn > 0 ? string.Format(" (decidido en la operación {0})", decididaEn) : "");
			if (estado != Validacion.Sigue && estado != ultimoEstadoValidacion && ultimoEstadoValidacion != null)
				Avisar("Validacion", mensaje);
			else
				Print(mensaje);
			ultimoEstadoValidacion = estado;
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
			if (FiltroGap && (sesion.Apertura <= 0 || sesion.CierreAnterior <= 0 || (sesion.Apertura - sesion.CierreAnterior) * lado <= 0))
				return false;
			if (FiltroSobreApertura && (sesion.Apertura <= 0 || (Close[0] - sesion.Apertura) * lado <= 0))
				return false;
			if (FiltroPrimeraVela && sesion.PrimeraVela != lado)
				return false;
			if (FiltroSobreCierreAnterior && (sesion.CierreAnterior <= 0 || (Close[0] - sesion.CierreAnterior) * lado <= 0))
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
			if (order == null)
				return;
			if (orderState == OrderState.Rejected && State == State.Realtime)
				Avisar("Rechazo", string.Format("Orden rechazada: {0} {1} | {2} | {3}", order.Name, order.OrderAction, error, comment));
			if (!Diagnostico)
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
		[Range(0, double.MaxValue)]
		[Display(Name="MaxRiesgoOperacionUSD", Description="No entrar si stop x contratos arriesga más que esto. 0 = apagado", Order=6, GroupName="4. Riesgo")]
		public double MaxRiesgoOperacionUSD
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name="FrenoOperaciones", Description="Cuántas operaciones recientes se miran. 0 = freno apagado", Order=1, GroupName="5. Freno y validación")]
		public int FrenoOperaciones
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name="FrenoAciertosMin", Description="Si los aciertos de esas operaciones caen bajo este %, se deja de operar en real", Order=2, GroupName="5. Freno y validación")]
		public double FrenoAciertosMin
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name="FrenoAciertosReanudar", Description="Con el freno activo, se vuelve a operar en real cuando la ventana (con las operaciones en simulación) llega a este %", Order=3, GroupName="5. Freno y validación")]
		public double FrenoAciertosReanudar
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, 99)]
		[Display(Name="ValidacionMinimo", Description="Punto de equilibrio en % de aciertos (hipótesis 'sin ventaja')", Order=4, GroupName="5. Freno y validación")]
		public double ValidacionMinimo
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, 99)]
		[Display(Name="ValidacionEsperado", Description="Aciertos que mostró el backtest (hipótesis 'hay ventaja')", Order=5, GroupName="5. Freno y validación")]
		public double ValidacionEsperado
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="FiltroGap", Description="Comprar solo si la apertura de las 9:30 abrió sobre el cierre de las 16:00 anterior (vender, debajo)", Order=7, GroupName="1. Entradas")]
		public bool FiltroGap
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="FiltroSobreApertura", Description="Comprar solo con el precio sobre la apertura de las 9:30 (vender, debajo)", Order=8, GroupName="1. Entradas")]
		public bool FiltroSobreApertura
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="FiltroPrimeraVela", Description="Comprar solo si la primera vela después de las 9:30 cerró alcista (vender, bajista)", Order=9, GroupName="1. Entradas")]
		public bool FiltroPrimeraVela
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="FiltroSobreCierreAnterior", Description="Comprar solo con el precio sobre el cierre de las 16:00 anterior (vender, debajo)", Order=10, GroupName="1. Entradas")]
		public bool FiltroSobreCierreAnterior
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name="SalidaTiempoVelas", Description="Tras estas velas en la operación, salir si no va al menos SalidaTiempoMinTicks a favor. 0 = apagado", Order=8, GroupName="2. Salidas")]
		public int SalidaTiempoVelas
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="SalidaTiempoMinTicks", Description="Ticks a favor que debe llevar para seguir (0 = salir solo si va perdiendo)", Order=9, GroupName="2. Salidas")]
		public int SalidaTiempoMinTicks
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="BloquearTrasRollover", Description="No abrir operaciones en un contrato que ya pasó su rollover", Order=1, GroupName="7. Protecciones en vivo")]
		public bool BloquearTrasRollover
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="CerrarPosicionHuerfana", Description="Si la cuenta tiene una posición que la estrategia no maneja, cerrarla a mercado (si no, solo avisa)", Order=2, GroupName="7. Protecciones en vivo")]
		public bool CerrarPosicionHuerfana
		{ get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name="MinutosNoticias", Description="Sin entradas desde X minutos antes hasta X después de cada noticia del archivo _noticias.csv. 0 = apagado", Order=3, GroupName="7. Protecciones en vivo")]
		public int MinutosNoticias
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

		// Sin [NinjaScriptProperty] a propósito: así el token no aparece en el nombre de la estrategia en el gráfico.
		[PasswordPropertyText(true)]
		[Display(Name="TelegramToken", Description="Token del bot de Telegram (de @BotFather). Vacío = sin Telegram", Order=4, GroupName="6. Registro y avisos")]
		public string TelegramToken
		{ get; set; }

		[Display(Name="TelegramChatId", Description="Chat donde llegan los avisos y el resumen diario", Order=5, GroupName="6. Registro y avisos")]
		public string TelegramChatId
		{ get; set; }

		[Display(Name="UrlLatido", Description="URL de un vigilante externo (p. ej. healthchecks.io) que recibe el estado en cada vela de la ventana. Vacío = apagado", Order=6, GroupName="6. Registro y avisos")]
		public string UrlLatido
		{ get; set; }
		#endregion

		// Ventana deslizante de aciertos con pausa y reanudación. Agregar devuelve 1 si acaba de pausar,
		// -1 si acaba de reanudar y 0 si nada cambió.
		public class FrenoAciertos
		{
			private readonly List<bool> resultados = new List<bool>();
			private readonly int ventana;
			private readonly double minimo;
			private readonly double reanudar;

			public FrenoAciertos(int ventana, double minimo, double reanudar)
			{
				this.ventana = ventana;
				this.minimo = minimo;
				this.reanudar = Math.Max(minimo, reanudar);
			}

			public bool Pausado { get; private set; }

			public double AciertosVentana
			{
				get
				{
					int n = Math.Min(ventana, resultados.Count);
					if (n <= 0)
						return 0;
					int ganadas = 0;
					for (int i = resultados.Count - n; i < resultados.Count; i++)
						if (resultados[i])
							ganadas++;
					return 100.0 * ganadas / n;
				}
			}

			public int Agregar(bool gano)
			{
				resultados.Add(gano);
				if (ventana <= 0 || resultados.Count < ventana)
					return 0;
				double aciertos = AciertosVentana;
				if (!Pausado && aciertos < minimo)
				{
					Pausado = true;
					return 1;
				}
				if (Pausado && aciertos >= reanudar)
				{
					Pausado = false;
					return -1;
				}
				return 0;
			}
		}

		// Operación que no se envía al mercado: se sigue con el alto y el bajo de cada vela. Si una vela
		// toca stop y objetivo a la vez, se asume que primero tocó el stop.
		public class OperacionSombra
		{
			private readonly int lado;
			private readonly double distStop, distObjetivo, slippage, tick;
			private bool abierta;
			private double entrada, stop, objetivo;

			public OperacionSombra(int lado, double distStop, double distObjetivo, double slippage, double tick)
			{
				this.lado = lado;
				this.distStop = distStop;
				this.distObjetivo = distObjetivo;
				this.slippage = slippage;
				this.tick = tick;
			}

			// Se llama al cierre de cada vela posterior a la señal: 1 ganó, -1 perdió, 0 sigue abierta.
			public int Actualizar(double abre, double alto, double bajo, double cierra, bool finHorario)
			{
				if (!abierta)
				{
					entrada = abre + slippage * lado;
					stop = entrada - distStop * lado;
					objetivo = entrada + distObjetivo * lado;
					abierta = true;
				}
				bool tocaStop = lado == 1 ? bajo <= stop : alto >= stop;
				bool tocaObjetivo = lado == 1 ? alto >= objetivo + tick : bajo <= objetivo - tick;
				if (tocaStop)
					return -1;
				if (tocaObjetivo)
					return 1;
				if (finHorario)
					return (cierra - entrada) * lado > 0 ? 1 : -1;
				return 0;
			}
		}

		// Apertura de las 9:30 y último cierre de las 16:00 de Nueva York, a partir de la hora de cierre de cada vela.
		public class SesionRTH
		{
			private DateTime fecha = DateTime.MinValue;
			private int hhmmAnterior = -1;
			private double cierre;

			public double Apertura { get; private set; }
			public double CierreAnterior { get; private set; }
			// Dirección de la primera vela después de las 9:30: 1 alcista, -1 bajista, 0 sin dato.
			public int PrimeraVela { get; private set; }

			public void Actualizar(DateTime horaNY, double abre, double cierra, double cierraPrevio)
			{
				int hhmm = horaNY.Hour * 100 + horaNY.Minute;
				if (horaNY.Date != fecha)
				{
					// La primera vela cargada puede caer a mitad de sesión: sin vela previa no se marca nada.
					hhmmAnterior = fecha == DateTime.MinValue ? -1 : 0;
					fecha = horaNY.Date;
					if (cierre > 0)
						CierreAnterior = cierre;
					Apertura = 0;
					PrimeraVela = 0;
				}
				if (hhmmAnterior >= 0 && hhmm > 930 && hhmmAnterior <= 930)
				{
					Apertura = abre;
					PrimeraVela = cierra > abre ? 1 : cierra < abre ? -1 : 0;
				}
				if (hhmmAnterior >= 0 && hhmm >= 1600 && hhmmAnterior < 1600)
					cierre = hhmm == 1600 ? cierra : cierraPrevio;
				hhmmAnterior = hhmm;
			}
		}

		// Estadística para validar en vivo con pocas operaciones.
		public static class Validacion
		{
			public const string Sigue = "falta evidencia, seguir en simulación";
			public const string Confirma = "VENTAJA CONFIRMADA frente al punto de equilibrio";
			public const string Descarta = "SIN VENTAJA: los aciertos se parecen más al punto de equilibrio";

			// Prueba secuencial de Wald (SPRT) con 5% de falsos positivos y 20% de falsos negativos.
			public static string Sprt(IList<bool> resultados, double p0, double p1, out int decididaEn)
			{
				double a = Math.Log((1 - 0.20) / 0.05), b = Math.Log(0.20 / (1 - 0.05));
				double siGana = Math.Log(p1 / p0), siPierde = Math.Log((1 - p1) / (1 - p0));
				double s = 0;
				for (int i = 0; i < resultados.Count; i++)
				{
					s += resultados[i] ? siGana : siPierde;
					if (s >= a || s <= b)
					{
						decididaEn = i + 1;
						return s >= a ? Confirma : Descarta;
					}
				}
				decididaEn = 0;
				return Sigue;
			}

			// Intervalo de Wilson al 95% para el porcentaje de aciertos.
			public static void Wilson(int ganadas, int n, out double bajo, out double alto)
			{
				const double z = 1.96;
				double p = (double)ganadas / n, z2 = z * z;
				double centro = (p + z2 / (2 * n)) / (1 + z2 / n);
				double margen = z * Math.Sqrt(p * (1 - p) / n + z2 / (4.0 * n * n)) / (1 + z2 / n);
				bajo = centro - margen;
				alto = centro + margen;
			}
		}
	}
}
