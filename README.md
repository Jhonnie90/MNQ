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

Para volver al bot original: HoraInicio 830, HoraFin 1000 (era 7:30–9:00 hora Colombia en verano), StopTicks 16, ProfitTicks 24, FiltroVWAP no, LimitePerdidaDiaUSD 20, LimiteGananciaDiaUSD 20.

## Qué se cambió y por qué

1. **El stop de 16 y el objetivo de 24 ticks eran ruido.** Entre 7:30 y 10:30 (hora Colombia) una vela de 1 minuto de MNQ se mueve en promedio 88 ticks, y una de 5 minutos 149. Stop y objetivo (40 ticks en total) caían dentro de la misma vela: en 17 de 29 operaciones no había forma de saber cuál se tocó primero.
2. **El backtest de NinjaTrader en modo Standard engañaba.** Con esas 17 velas ambiguas, el modo Standard mostraba unos 55% de aciertos y ganancia. Revisando minuto a minuto el bot real daba 21–27% y perdía. Ahora la estrategia usa `OrderFillResolution = High` con velas de 1 minuto.
3. **Horario.** La ventana original (7:30–9:00 en Colombia) caía en las noticias de las 8:30 de Nueva York y el mercado antes de la apertura. Operar desde las 9:35 de Nueva York mejoró los aciertos en todas las combinaciones probadas.
4. **Stop más ancho que el objetivo.** Es lo que más sube el porcentaje de aciertos. El costo: cada pérdida borra unas dos ganancias, así que **el bot necesita más de 68% de aciertos para no perder** (con comisión de $1.24 y 1 tick de slippage).
5. **Filtro VWAP**, horario fijo en Nueva York, límites diarios configurables, opción de ventas y de salidas por ATR.
6. Al pasar a tiempo real se reinician los contadores del día, para que las operaciones simuladas sobre el histórico no bloqueen el día real. Ojo: si se reinicia la estrategia a mitad del día, el contador vuelve a cero.

## Resultados de la investigación

Datos de Yahoo Finance (contrato continuo `MNQ=F`), 1 contrato, comisión de $1.24 ida y vuelta, 1 tick de slippage en entradas y stops. Los parámetros se eligieron con datos del 12 de agosto al 23 de septiembre de 2026 y se validaron del 24 de septiembre al 22 de octubre, con salidas revisadas minuto a minuto.

| Bot | Periodo | Ops | Aciertos | Neto | Factor de ganancia | Peor racha |
|---|---|---|---|---|---|---|
| Original | 12 ago – 22 oct | 29 | 20.7% | −$159 | 0.29 | −$170 |
| Original | 24 sep – 22 oct (minuto a minuto) | 11 | 27.3% | −$46 | 0.41 | −$58 |
| **Nuevo** | 12 ago – 22 oct | 24 | **79.2%** | +$238 | 1.77 | −$132 |
| **Nuevo** | 24 sep – 22 oct (minuto a minuto) | 5 | 100% | +$144 | — | $0 |

**Cómo leer esto, sin engañarse:**

- 24 operaciones son muy pocas. Con esa muestra, un 79% real podría estar en cualquier punto entre más o menos 60% y 92%, y el punto de equilibrio es 68%. Es prometedor, no probado.
- Los 5 de 5 de la validación no prueban nada por sí solos: son muy pocos.
- Hubo 360 combinaciones probadas, y entre tantas algunas salen bien por suerte. Lo que sí se repitió en casi todas: el horario después de la apertura, el stop más ancho que el objetivo y solo compras.
- En la validación el MNQ bajó 5.2% y el bot aguantó, así que no fue solo un mercado alcista.

La investigación está en [`investigacion/`](investigacion/). `motor.py` imita la ejecución de NinjaTrader (señal al cierre de la vela, entrada en la siguiente, slippage y comisión).

```bash
cd investigacion
pip install -r requirements.txt
python descargar.py      # velas de 2, 5 y 15 min (60 días)
python descargar_1m.py   # velas de 1 min (30 días)
python final.py          # original vs. nuevo
python busqueda.py       # la búsqueda completa
```

## De aquí a plata real: lo que falta

1. **Compilar en NinjaTrader** (F5). Si algo no compila, lo más probable es la línea de la zona horaria (`Core.Globals.GeneralOptions.TimeZoneInfo`).
2. **Backtest largo en Strategy Analyzer:** 2 años o más, MNQ en 5 minutos, Order fill resolution = High con **1 tick** si hay datos de ticks y comisiones activadas. La pregunta: ¿los aciertos se mantienen por encima de 68%?
3. **Walk-forward** (Strategy Analyzer → Walk Forward Optimization) sobre StopTicks, ProfitTicks y HoraInicio, para ver si los valores buenos se sostienen en el tiempo.
4. **Market Replay (Playback)** de varias semanas recientes.
5. **4 semanas en Sim101** con datos en vivo, comparando aciertos y slippage reales contra el backtest.
6. Solo entonces, **1 contrato real**, y subir solo si los resultados en vivo se parecen a los de la prueba.
