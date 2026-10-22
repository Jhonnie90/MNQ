# MNQ

Estrategia de NinjaTrader 8 para el micro Nasdaq (MNQ): compra cuando la EMA 9 cruza hacia arriba la EMA 21, solo después de la apertura de Nueva York y solo si el precio está por encima del VWAP de la sesión. Cada entrada sale con stop y objetivo fijos, y todo se cierra a una hora fija.

> Esto es una estrategia experimental. Todo lo de abajo se probó en simulación con unos 50 días de datos. No es una garantía de resultados ni una recomendación de inversión.

## Instalación

1. Copiar `MNQAutomaticoPrueba.cs` a `Documentos\NinjaTrader 8\bin\Custom\Strategies\`.
2. En NinjaTrader: **New → NinjaScript Editor**, abrir la estrategia y compilar con **F5**.
3. Gráfico de **MNQ en velas de 5 minutos**, plantilla de horario **CME US Index Futures ETH**.
4. Agregar la estrategia al gráfico con la cuenta **Sim101** primero.

Las horas del bot son de **Nueva York**: el código convierte la hora del gráfico, así que no importa la zona horaria del PC ni el cambio de hora de EE. UU. (en invierno las 9:35 de Nueva York son las 9:35 de Colombia, y en verano las 8:35).

## Parámetros por defecto

| Grupo | Parámetro | Valor | Qué hace |
|---|---|---|---|
| Entradas | EmaRapida / EmaLenta | 9 / 21 | Cruce que dispara la entrada |
| | PermitirLargos / PermitirCortos | sí / no | Solo compras (las ventas empeoraron los aciertos) |
| | FiltroVWAP | sí | Compra solo con el precio sobre el VWAP de la sesión |
| | EmaTendencia | 0 | Filtro extra opcional (p. ej. 50). 0 = apagado |
| Salidas | StopTicks / ProfitTicks | 120 / 60 | Stop de 30 puntos ($60 por contrato), objetivo de 15 puntos ($30) |
| | UsarATR, AtrPeriodo, StopATR, ProfitATR | no, 14, 2, 1 | Alternativa: stop y objetivo según la volatilidad |
| Horario (Nueva York) | HoraInicio / HoraFin | 935 / 1130 | Entradas desde las 9:35; a las 11:30 se cierra todo |
| Riesgo | Contratos | 1 | |
| | MaxOperacionesDia | 3 | |
| | LimitePerdidaDiaUSD | 120 | Dos stops y se apaga por el día |
| | LimiteGananciaDiaUSD | 0 | Sin tope de ganancia |
| | MaxATRvsStop | 2.0 | No entra si la vela promedio (ATR 14) supera el doble del stop: el stop quedaría metido en el ruido |
| Freno por aciertos | FrenoOperaciones / FrenoAciertosMin / FrenoDiasPausa | 20 / 68% / 5 | Si de las últimas 20 operaciones acierta menos del 68% (el punto de equilibrio), se pausa 5 días y avisa |
| Registro y avisos | RegistrarCSV | sí | Guarda cada operación real en `Documentos\NinjaTrader 8\MNQAutomaticoPrueba_operaciones.csv` |
| | Diagnostico | sí | Detalle de cada orden en la ventana Output. **Apagarlo para optimizar**: acelera mucho el Strategy Analyzer |
| | DiasAvisoRollover | 10 | Avisa cuando se acerca el cambio de contrato |

Para volver al bot original: HoraInicio 830, HoraFin 1000 (era 7:30–9:00 hora Colombia en verano), StopTicks 16, ProfitTicks 24, FiltroVWAP no, LimitePerdidaDiaUSD 20, LimiteGananciaDiaUSD 20, MaxATRvsStop 0, FrenoOperaciones 0.

## Qué se cambió y por qué

1. **El stop de 16 y el objetivo de 24 ticks eran ruido.** Entre 7:30 y 10:30 (hora Colombia) una vela de 1 minuto de MNQ se mueve en promedio 88 ticks, y una de 5 minutos 149. Stop y objetivo (40 ticks en total) caían dentro de la misma vela: en 17 de 29 operaciones no había forma de saber cuál se tocó primero.
2. **El backtest de NinjaTrader en modo Standard engañaba.** Con esas 17 velas ambiguas, el modo Standard mostraba unos 55% de aciertos y ganancia. Revisando minuto a minuto el bot real daba 21–27% y perdía. Ahora la estrategia usa `OrderFillResolution = High` con velas de 1 minuto.
3. **Horario.** La ventana original (7:30–9:00 en Colombia) caía en las noticias de las 8:30 de Nueva York y el mercado antes de la apertura. Operar desde las 9:35 de Nueva York mejoró los aciertos en todas las combinaciones probadas.
4. **Stop más ancho que el objetivo.** Es lo que más sube el porcentaje de aciertos. El costo: cada pérdida borra unas dos ganancias, así que **el bot necesita más de 68% de aciertos para no perder** (con comisión de $1.24 y 1 tick de slippage).
5. **Filtro VWAP**, horario fijo en Nueva York, límites diarios configurables, opción de ventas y de salidas por ATR.
6. Al pasar a tiempo real se ignoran las operaciones simuladas sobre el histórico de hoy, y se cargan las **operaciones reales de hoy desde el CSV**. Así, si la estrategia se reinicia a mitad del día, los límites diarios siguen contando.

## Protecciones

- **Límite diario a prueba de reinicios.** Cada operación real (cuenta Sim101, Playback o real) se agrega al CSV apenas se cierra. Al activar la estrategia se suman las de hoy de la misma cuenta e instrumento, así que dos pérdidas seguidas de un reinicio siguen bloqueando el día.
- **Freno por aciertos.** El bot necesita más de 68% de aciertos para no perder plata. Si en las últimas 20 operaciones queda por debajo, deja de entrar 5 días y lanza una alerta con sonido. Después de la pausa necesita 20 operaciones nuevas antes de volver a evaluar. En tiempo real la ventana incluye las operaciones simuladas sobre el histórico cargado en el gráfico, así que se recalcula sola al reiniciar.
- **Filtro de volatilidad.** En la muestra solo descartó 1 de 24 operaciones: es una red de seguridad para días extremos, no una mejora de resultados. Con el bot original (stop de 16 ticks) habría bloqueado todas las entradas, que es justo el caso que debe atajar.
- **Registro CSV** para comparar en vivo contra el backtest. Columnas: FechaNY, Cuenta, Instrumento, Lado, Cantidad, EntradaNY, PrecioSenal, PrecioEntrada, SlippageTicks, SalidaNY, PrecioSalida, Motivo, GananciaUSD, MAETicks, MFETicks (MAE y MFE: lo máximo que la operación fue en contra y a favor). Se abre en Excel.
- **Aviso de rollover.** Los futuros de índices de CME vencen el tercer viernes de marzo, junio, septiembre y diciembre, y el volumen se pasa al siguiente contrato 8 días antes. Para MNQ 12-26 ese día es el **10 de diciembre de 2026**. Desde 10 días antes el bot avisa una vez al día que hay que pasar el gráfico al siguiente contrato (MNQ 03-27). No cambia de contrato solo.
- **Alertas.** Bloqueo diario, freno y rollover aparecen en la ventana Alerts de NinjaTrader con sonido (solo en tiempo real).

## Resultados de la investigación

Datos de Yahoo Finance (contrato continuo `MNQ=F`), 1 contrato, comisión de $1.24 ida y vuelta, 1 tick de slippage en entradas y stops. Los parámetros se eligieron con datos del 12 de agosto al 23 de septiembre de 2026 y se validaron del 24 de septiembre al 22 de octubre, con salidas revisadas minuto a minuto.

| Bot | Periodo | Ops | Aciertos | Neto | Factor de ganancia | Peor racha |
|---|---|---|---|---|---|---|
| Original | 12 ago – 22 oct | 29 | 20.7% | −$159 | 0.29 | −$170 |
| Original | 24 sep – 22 oct (minuto a minuto) | 11 | 27.3% | −$46 | 0.41 | −$58 |
| **Nuevo** | 12 ago – 22 oct | 23 | **82.6%** | +$299 | 2.21 | −$123 |
| **Nuevo** | 24 sep – 22 oct (minuto a minuto) | 5 | 100% | +$144 | — | $0 |

Sin el filtro de volatilidad eran 24 operaciones con 79.2% de aciertos: el filtro solo quitó una pérdida. Esa diferencia no significa nada estadísticamente.

**Cómo leer esto, sin engañarse:**

- 24 operaciones son muy pocas. Con esa muestra, un 79% real podría estar en cualquier punto entre más o menos 60% y 92%, y el punto de equilibrio es 68%. Es prometedor, no probado.
- Los 5 de 5 de la validación no prueban nada por sí solos: son muy pocos.
- Hubo 360 combinaciones probadas, y entre tantas algunas salen bien por suerte. Lo que sí se repitió en casi todas: el horario después de la apertura, el stop más ancho que el objetivo y solo compras.
- En la validación el MNQ bajó 5.2% y el bot aguantó, así que no fue solo un mercado alcista.

### Pruebas de robustez

- **Contra entrar al azar.** Con un stop del doble del objetivo, uno esperaría acertar 2 de cada 3 veces por pura geometría. En la práctica, entrar al azar en el mismo horario y con las mismas salidas acierta 59–60% y pierde plata (300 simulaciones). El bot le gana a 98 de cada 100. Ojo: la configuración salió como la mejor de 360, así que parte de esa ventaja puede ser suerte de la selección.
- **Estabilidad.** Con stops de 60 a 200 ticks y objetivos de 50 a 80, casi todo gana. Mover la hora de inicio entre 9:35 y 10:00 deja los aciertos en 79–87%. Las EMAs vecinas de 9/21 dan 75–91%; en las esquinas (12/26, 11/30) cae a 55%.
- **Slippage.** Con 4 ticks en contra el neto baja de $238 a $230: casi no afecta.
- **Otros índices: no se sostiene.** Con stops ajustados a la volatilidad de cada uno: S&P (MES) 65% de aciertos con factor de ganancia 0.73, Russell (M2K) 64% con 0.78, Dow (MYM) 37% con 0.24. El S&P se mueve casi igual que el Nasdaq. Si la ventaja fuera sólida debería verse algo ahí, así que es la señal más fuerte de que parte del 79% puede ser suerte de estos 50 días.
- **Rachas.** Aun con 79% real de aciertos, en un año (unas 250 operaciones) lo normal es una racha de 3 pérdidas seguidas, y hasta 5 de vez en cuando (unos $310 con 1 contrato).

La investigación está en [`investigacion/`](investigacion/). `motor.py` imita la ejecución de NinjaTrader (señal al cierre de la vela, entrada en la siguiente, slippage y comisión).

```bash
cd investigacion
pip install -r requirements.txt
python descargar.py      # velas de 2, 5 y 15 min (60 días)
python descargar_1m.py   # velas de 1 min (30 días)
python final.py          # original vs. nuevo
python busqueda.py       # la búsqueda completa
python analisis_azar.py          # contra entradas al azar
python analisis_estabilidad.py   # stop/objetivo, slippage, horario y EMAs
python analisis_volatilidad.py   # calibración del filtro de volatilidad
python analisis_otros.py         # MES, MYM y M2K (descarga sus datos)
```

## De aquí a plata real: lo que falta

1. **Compilar en NinjaTrader** (F5). El código se verificó contra una imitación de la API, no contra NinjaTrader real. Si algo no compila, lo más probable son las llamadas propias de NinjaTrader menos comunes: la zona horaria (`Core.Globals.GeneralOptions.TimeZoneInfo`), las carpetas (`Core.Globals.UserDataDir`, `Core.Globals.InstallDir`), `Alert(...)` o `Trade.MaeTicks` / `MfeTicks`.
2. **Backtest largo en Strategy Analyzer:** 2 años o más, MNQ en 5 minutos, Order fill resolution = High con **1 tick** si hay datos de ticks y comisiones activadas. La pregunta: ¿los aciertos se mantienen por encima de 68%?
3. **Walk-forward** (Strategy Analyzer → Walk Forward Optimization) sobre StopTicks, ProfitTicks y HoraInicio, para ver si los valores buenos se sostienen en el tiempo.
4. **Market Replay (Playback)** de varias semanas recientes.
5. **4 semanas en Sim101** con datos en vivo, comparando aciertos y slippage reales contra el backtest.
6. Solo entonces, **1 contrato real**, y subir solo si los resultados en vivo se parecen a los de la prueba.
