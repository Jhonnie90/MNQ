# MNQ

Estrategia de NinjaTrader 8 para el micro Nasdaq (MNQ): compra cuando la EMA 9 cruza hacia arriba la EMA 21, solo después de la apertura de Nueva York y solo si el precio está por encima del VWAP de la sesión. Cada entrada sale con stop y objetivo fijos, y todo se cierra a una hora fija.

> Esto es una estrategia experimental. Todo lo de abajo se probó en simulación con unos 50 días de datos. No es una garantía de resultados ni una recomendación de inversión. **Con esta muestra todavía no está demostrado que tenga ventaja** (ver [Resultados](#resultados-de-la-investigación)).

## Instalación

1. Copiar `MNQAutomaticoPrueba.cs` a `Documentos\NinjaTrader 8\bin\Custom\Strategies\`.
2. En NinjaTrader: **New → NinjaScript Editor**, abrir la estrategia y compilar con **F5**.
3. Gráfico de **MNQ en velas de 5 minutos**, plantilla de horario **CME US Index Futures ETH**.
4. Agregar la estrategia al gráfico con la cuenta **Sim101** primero.

Las horas del bot son de **Nueva York**: el código convierte la hora del gráfico, así que no importa la zona horaria del PC ni el cambio de hora de EE. UU. Desde el 1 de noviembre de 2026 las 9:35 de Nueva York son las 9:35 de Colombia (en verano son las 8:35). Se probó con fechas a ambos lados del cambio.

## Parámetros por defecto

| Grupo | Parámetro | Valor | Qué hace |
|---|---|---|---|
| 1. Entradas | EmaRapida / EmaLenta | 9 / 21 | Cruce que dispara la entrada |
| | PermitirLargos / PermitirCortos | sí / no | Solo compras (las ventas empeoraron los aciertos) |
| | FiltroVWAP | sí | Compra solo con el precio sobre el VWAP de la sesión |
| | EmaTendencia | 0 | Filtro opcional (p. ej. 50). 0 = apagado |
| | FiltroGap, FiltroSobreApertura, FiltroPrimeraVela, FiltroSobreCierreAnterior | no | Filtros opcionales para probar con años de datos (ver [Probado](#probado-con-los-50-días)) |
| 2. Salidas | StopTicks / ProfitTicks | 120 / 60 | Stop de 30 puntos ($60 por contrato), objetivo de 15 puntos ($30) |
| | UsarATR, AtrPeriodo, StopATR, ProfitATR | no, 14, 2, 1 | Alternativa: stop y objetivo según la volatilidad |
| | SalidaTiempoVelas / SalidaTiempoMinTicks | 0 / 0 | Opcional: tras N velas, salir si no va X ticks a favor |
| 3. Horario (Nueva York) | HoraInicio / HoraFin | 935 / 1130 | Entradas desde las 9:35; a las 11:30 se cierra todo |
| 4. Riesgo | Contratos | 1 | |
| | MaxOperacionesDia | 3 | |
| | LimitePerdidaDiaUSD / LimiteGananciaDiaUSD | 120 / 0 | Dos stops y se apaga por el día; sin tope de ganancia |
| | MaxATRvsStop | 2.0 | No entra si la vela promedio (ATR 14) supera el doble del stop |
| | MaxRiesgoOperacionUSD | 0 | Opcional: no entra si stop × contratos arriesga más que esto |
| 5. Freno y validación | FrenoOperaciones / FrenoAciertosMin / FrenoAciertosReanudar | 50 / 64% / 68% | Ver [Freno](#freno-por-aciertos) |
| | ValidacionMinimo / ValidacionEsperado | 68% / 79% | Punto de equilibrio y acierto del backtest, para la [validación en vivo](#validación-en-vivo) |
| 6. Registro y avisos | RegistrarCSV | sí | Guarda cada operación real en `Documentos\NinjaTrader 8\MNQAutomaticoPrueba_operaciones.csv` |
| | Diagnostico | sí | Detalle de cada orden en la ventana Output. **Apagarlo para optimizar**: acelera mucho el Strategy Analyzer |
| | DiasAvisoRollover | 10 | Avisa cuando se acerca el cambio de contrato |
| | TelegramToken / TelegramChatId | vacío | Avisos al celular (ver [Avisos](#avisos-al-celular-y-vigilante-externo)) |
| | UrlLatido | vacío | Vigilante externo que avisa si el bot se cae |
| 7. Protecciones en vivo | BloquearTrasRollover | sí | No abre operaciones en un contrato que ya pasó su rollover |
| | CerrarPosicionHuerfana | no | Si la cuenta tiene una posición que la estrategia no maneja: no (solo avisa y no entra) o sí (la cierra) |
| | MinutosNoticias | 15 | Sin entradas 15 min antes y después de cada noticia del archivo de noticias (sin archivo no hace nada) |

Para volver al bot original: HoraInicio 830, HoraFin 1000 (era 7:30–9:00 hora Colombia en verano), StopTicks 16, ProfitTicks 24, FiltroVWAP no, LimitePerdidaDiaUSD 20, LimiteGananciaDiaUSD 20, MaxATRvsStop 0, FrenoOperaciones 0.

## Qué se cambió y por qué

1. **El stop de 16 y el objetivo de 24 ticks eran ruido.** Entre 7:30 y 10:30 (hora Colombia) una vela de 1 minuto de MNQ se mueve en promedio 88 ticks, y una de 5 minutos 149. Stop y objetivo (40 ticks en total) caían dentro de la misma vela: en 17 de 29 operaciones no había forma de saber cuál se tocó primero.
2. **El backtest de NinjaTrader en modo Standard engañaba.** Con esas 17 velas ambiguas, el modo Standard mostraba unos 55% de aciertos y ganancia. Revisando minuto a minuto el bot real daba 21–27% y perdía. Ahora la estrategia usa `OrderFillResolution = High` con velas de 1 minuto.
3. **Horario.** La ventana original (7:30–9:00 en Colombia) caía en las noticias de las 8:30 de Nueva York y el mercado antes de la apertura. Operar desde las 9:35 de Nueva York mejoró los aciertos en todas las combinaciones probadas.
4. **Stop más ancho que el objetivo.** Es lo que más sube el porcentaje de aciertos. El costo: cada pérdida borra unas dos ganancias, así que **el bot necesita más de 68% de aciertos para no perder** (con comisión de $1.24 y 1 tick de slippage).
5. **Filtro VWAP**, horario fijo en Nueva York, límites diarios configurables, opción de ventas y de salidas por ATR.
6. **Protecciones para operar en vivo** (sección siguiente), tomadas de lo que hacen los bots serios.

## Protecciones

### Freno por aciertos

El bot necesita más de 68% de aciertos para no perder plata. El freno mira las últimas **50** operaciones: si aciertan menos del **64%**, deja de operar en real y lanza una alerta. Mientras está activo, las señales se siguen **en simulación** (sin enviar órdenes), y cuando las últimas 50 operaciones, contando las simuladas, vuelven al **68%**, se reanuda solo.

Por qué esos números (simulación de 250 operaciones, unas un año, en `investigacion/analisis_freno.py`):

| Freno | Salta por mala suerte aunque el bot sí tenga 79% | Si el acierto real cae a 65%, lo detecta en |
|---|---|---|
| 20 operaciones, menos de 68% (la versión anterior) | 96% de las veces | 20 operaciones |
| **50 operaciones, menos de 64%** | **10%** | 56 operaciones (el 99% de las veces) |

El freno anterior saltaba casi siempre por pura casualidad. La operación en simulación evita una pausa fija: la estrategia vuelve cuando los datos lo justifican.

### Validación en vivo

Después de cada operación real, el bot imprime en la ventana Output una línea como esta:

```
Validación en vivo (Sim101): 41 de 50 aciertos (82.0%), rango probable 69-90% | falta evidencia, seguir en simulación
```

Usa una prueba secuencial (SPRT) que compara dos hipótesis: el bot acierta como en el backtest (79%) o está en el punto de equilibrio (68%). Cuando hay evidencia suficiente avisa **VENTAJA CONFIRMADA** o **SIN VENTAJA**. Con 79% real lo confirma el 83% de las veces, en unas 57 operaciones; con 68% real la descarta el 96% de las veces. Lo mismo se puede calcular desde el CSV o desde un export de NinjaTrader con `investigacion/validacion.py`.

Regla sugerida: no pasar de Sim101 a dinero real hasta que diga **VENTAJA CONFIRMADA**.

### Límite diario y posición de la cuenta

- **El límite diario sobrevive a un reinicio.** Cada operación real se agrega al CSV apenas se cierra. Al activar la estrategia se suman las de hoy de la misma cuenta e instrumento, así que dos pérdidas seguidas de un reinicio siguen bloqueando el día.
- **No apila contratos.** Si la cuenta tiene una posición en MNQ que la estrategia no maneja (por ejemplo, porque se reactivó a mitad de una operación), no entra y avisa. Con `CerrarPosicionHuerfana` activado, además la cierra a mercado.
- **Alerta si no quedó plana.** Si dos velas después de las 11:30 la estrategia sigue en posición, avisa.

### Protocolo de reinicio seguro

Al reactivar la estrategia, NinjaTrader (con `WaitUntilFlat`) **cancela primero el stop y el objetivo** que protegían la posición real. Si NinjaTrader se cae con una operación abierta:

1. Mirar la cuenta en la app del bróker.
2. Si hay posición, **cerrarla a mano**.
3. Solo después, reactivar la estrategia.

### Rollover, noticias y volatilidad

- **Rollover.** Los futuros de índices de CME vencen el tercer viernes de marzo, junio, septiembre y diciembre, y el volumen pasa al siguiente contrato 8 días antes. Para MNQ 12-26 ese día es el **jueves 10 de diciembre de 2026**. Desde 10 días antes el bot avisa una vez al día. Desde ese día **no abre operaciones** en el contrato viejo. Para cambiar de contrato: esperar a estar plano, desactivar, pasar el gráfico a MNQ 03-27 y reactivar.
- **Noticias.** Copiar `datos/MNQAutomaticoPrueba_noticias_ejemplo.csv` a `Documentos\NinjaTrader 8\MNQAutomaticoPrueba_noticias.csv` y completarlo con fechas verificadas. Los datos de las 10:00 de Nueva York (ISM, JOLTS, confianza del consumidor) caen dentro de la ventana. La evidencia sobre evitar días de noticias es mixta, así que conviene medirlo en el backtest antes de usarlo (el backtest lee el mismo archivo). Es obligatorio en empresas de fondeo que prohíben operar durante noticias.
- **Volatilidad.** En la muestra el filtro ATR solo descartó 1 de 24 operaciones: es una red de seguridad para días extremos, no una mejora de resultados.

### Avisos al celular y vigilante externo

- **Telegram:** crear un bot con @BotFather, poner el token y el chat id en los parámetros. Llegan: cada operación cerrada (con su slippage), órdenes rechazadas, cambios de conexión, bloqueo diario, freno, rollover, cuenta descuadrada, validación, resumen del día y "estrategia detenida". El token queda en la configuración de NinjaTrader del PC, **no en este repositorio**, y no aparece en el nombre de la estrategia en el gráfico.
- **Vigilante (latido):** un bot caído no puede avisar que se cayó, y NinjaTrader no reactiva estrategias solo después de un reinicio. Con `UrlLatido` el bot envía su estado en cada vela, desde 30 minutos antes de la ventana hasta 15 después, a un servicio externo. Si deja de llegar, el servicio avisa. Sirve por ejemplo un check gratuito de healthchecks.io programado para los días hábiles de 9:05 a 11:45 de Nueva York, o un servidor propio.
- **Conexión:** un corte de menos de 30 segundos no detiene la estrategia (`DisconnectDelaySeconds = 30`).

### Registro CSV

Columnas: FechaNY, Cuenta, Instrumento, Lado, Cantidad, EntradaNY, PrecioSenal, PrecioEntrada, SlippageTicks, SalidaNY, PrecioSalida, Motivo, GananciaUSD, MAETicks, MFETicks (MAE y MFE: lo máximo que la operación fue en contra y a favor). Se abre en Excel.

## Resultados de la investigación

Datos de Yahoo Finance (contrato continuo `MNQ=F`), 1 contrato, comisión de $1.24 ida y vuelta, 1 tick de slippage en entradas y stops. Los parámetros se eligieron con datos del 12 de agosto al 23 de septiembre de 2026 y se validaron del 24 de septiembre al 22 de octubre, con salidas revisadas minuto a minuto.

| Bot | Periodo | Ops | Aciertos | Neto | Factor de ganancia | Peor racha |
|---|---|---|---|---|---|---|
| Original | 12 ago – 22 oct | 29 | 20.7% | −$159 | 0.29 | −$170 |
| Original | 24 sep – 22 oct (minuto a minuto) | 11 | 27.3% | −$46 | 0.41 | −$58 |
| **Nuevo** | 12 ago – 22 oct | 23 | **82.6%** | +$299 | 2.21 | −$123 |
| **Nuevo** | 24 sep – 22 oct (minuto a minuto) | 5 | 100% | +$144 | — | $0 |

**Cómo leer esto, sin engañarse:**

- Con 19 de 24 aciertos, el acierto real podría estar entre 60% y 91%, y el punto de equilibrio es 68%. Si el bot no tuviera ventaja, un resultado así saldría el 17% de las veces. Para distinguir 79% de 68% hacen falta unas **100 operaciones** (unos 10 meses al ritmo actual).
- Los 5 de 5 de la validación no prueban nada por sí solos.
- **La selección pesa:** la configuración salió como la mejor de 360, con 75% de aciertos en el periodo de búsqueda. Repitiendo esa misma búsqueda 100 veces con señales **al azar**, la mejor de las 360 llega a 80% de mediana, y en 98 de cada 100 repeticiones a 75% o más (`investigacion/analisis_mejor_de_n.py`). O sea, el resultado de la búsqueda se explica por completo por haber elegido la mejor entre muchas. Solo la historia larga con la regla congelada puede decir si hay algo más.
- En la validación el MNQ bajó 5.2% y el bot aguantó, así que no fue solo un mercado alcista.

### Pruebas de robustez

- **Contra entrar al azar.** Con un stop del doble del objetivo, entrar al azar en el mismo horario y con las mismas salidas acierta 59–60% y pierde plata (300 simulaciones). La configuración elegida le gana a 98 de cada 100, pero eso no corrige la selección entre 360 (punto anterior).
- **Estabilidad.** Con stops de 60 a 200 ticks y objetivos de 50 a 80, casi todo gana. Mover la hora de inicio entre 9:35 y 10:00 deja los aciertos en 79–87%. Las EMAs vecinas de 9/21 dan 75–91%; en las esquinas (12/26, 11/30) cae a 55%.
- **Slippage.** Con 4 ticks en contra el neto baja de $238 a $230: casi no afecta.
- **Otros índices: no se sostiene.** Con stops ajustados a la volatilidad de cada uno: S&P (MES) 65% de aciertos con factor de ganancia 0.73, Russell (M2K) 64% con 0.78, Dow (MYM) 37% con 0.24.
- **Rachas.** Aun con 79% real de aciertos, en un año (unas 250 operaciones) lo normal es una racha de 3 pérdidas seguidas, y hasta 5 de vez en cuando (unos $310 con 1 contrato).

### Probado con los 50 días

| Idea | Resultado | Decisión |
|---|---|---|
| Salida por tiempo (1, 2, 3 o 6 velas) | Las ganadoras se resuelven tan rápido que casi nunca actúa; con 1 vela baja a 78% | Opción apagada, para probar con años de datos |
| Breakeven a +30 / +40 ticks | Igual o peor (78%) | No se incluyó |
| Stop de 90 o 100 ticks | Baja a 65–70% | Se mantiene 120 |
| Primera vela de 9:30 alcista | 75% con la mitad de operaciones | Opción apagada |
| Gap al alza | 86% con 14 operaciones | Opción apagada: muestra demasiado chica |
| Precio sobre la apertura de 9:30 | 83% con 18 operaciones | Opción apagada |
| Precio sobre el cierre anterior | 87% con 15 operaciones | Opción apagada |
| Entradas con orden límite, trailing stop, machine learning, martingala | No se probaron, por lo que dice la investigación (ver abajo) | No incluir |

Los filtros que mejoran un poco quedan como opciones apagadas a propósito: probar más filtros sobre los mismos 50 días solo agrega suerte. Hay que probarlos con historia larga y decidir antes de mirar.

### Lo que hacen los mejores sistemas (resumen de la investigación)

- **Ganan poco por operación, muchas veces y en muchos mercados.** Las firmas sistemáticas grandes combinan muchas señales débiles y no confían en una sola regla. El paso siguiente con más valor, si se confirma la ventaja, es sumar una segunda estrategia independiente (otro horario u otra lógica), no más filtros.
- **Las ventajas intradía documentadas son de momentum** y aciertan poco (24–48%), con ganadoras grandes. Un bot con 80% de aciertos tiene el perfil contrario: muchas ganancias chicas y pérdidas grandes. No es malo, pero hay que vigilar las colas.
- **Los cruces de medias por sí solos casi no tienen respaldo:** en un estudio de 7.846 reglas técnicas en velas de 5 minutos, ninguna sobrevivió a la corrección por búsqueda. El VWAP es la pieza con más respaldo.
- **Validar antes de pulir:** historia larga con la regla congelada, la misma vara que en vivo, y una prueba secuencial para decidir.
- **En vivo se pierde plata por reinicios, cortes y silencios, no por milisegundos.** Un VPS rápido no aporta en velas de 5 minutos; lo que importa es que el bot esté encendido y que avise.
- **Para empresas de fondeo:** en 2026, Apex prohíbe la automatización; Topstep la permite con condiciones y prohíbe VPS/VPN; MyFundedFutures prohíbe operar durante noticias de nivel 1. Las reglas cambian seguido: verificarlas antes.
- **No escalar todavía:** con el rango actual de aciertos, Kelly sale negativo. Seguir con 1 contrato hasta tener 150–250 operaciones con el límite inferior del rango por encima de 70%.

## Herramientas en `investigacion/`

`motor.py` imita la ejecución de NinjaTrader (señal al cierre de la vela, entrada en la siguiente, slippage y comisión).

```bash
cd investigacion
pip install -r requirements.txt
python descargar.py      # velas de 2, 5 y 15 min (60 días)
python descargar_1m.py   # velas de 1 min (30 días)
python final.py          # original vs. nuevo
python busqueda.py       # la búsqueda completa
python analisis_azar.py          # contra entradas al azar
python analisis_mejor_de_n.py    # el mejor de 360 con señales al azar
python analisis_estabilidad.py   # stop/objetivo, slippage, horario y EMAs
python analisis_volatilidad.py   # calibración del filtro de volatilidad
python analisis_otros.py         # MES, MYM y M2K (descarga sus datos)
python analisis_mejoras.py       # salidas por tiempo, breakeven y filtros de apertura
python analisis_freno.py         # falsas alarmas del freno y prueba secuencial
python validacion.py RUTA.csv --cuenta Sim101    # ¿alcanzan las operaciones? (CSV del bot o export de NinjaTrader)
python cotejar.py EXPORT_NINJATRADER.csv         # NinjaTrader vs. Python, operación por operación
```

## De aquí a plata real: lo que falta

1. **Compilar en NinjaTrader** (F5). El código se verificó contra una imitación de la API, no contra NinjaTrader real. Si algo no compila, lo más probable son las llamadas propias de NinjaTrader menos comunes: la zona horaria (`Core.Globals.GeneralOptions.TimeZoneInfo`), las carpetas (`Core.Globals.UserDataDir`, `Core.Globals.InstallDir`), `Alert(...)`, `Account.Flatten`, `OnConnectionStatusUpdate` o `Trade.MaeTicks` / `MfeTicks`.
2. **Revisar el horario el lunes 2 de noviembre:** la primera entrada posible debe ser a las 9:35 de Colombia.
3. **Cotejar** el backtest de NinjaTrader con `cotejar.py` en el mismo periodo: si no hacen las mismas operaciones, los números de arriba no son los del bot.
4. **Historia larga con la regla congelada (lo más importante).** NQ de 1 minuto desde 2008 (FirstRate Data o Portara, unos US$300) o MNQ desde mayo de 2019. Correr la regla exacta, sin reoptimizar, con comisión real, 1–2 ticks de slippage en el stop y resolución de llenado a 1 tick si hay datos. Reportar el acierto año por año y revisar 2020 y 2022. Pasa de 24 a cientos de operaciones: es lo único que separa ventaja de suerte.
5. **Walk-forward** (Strategy Analyzer → Walk Forward Optimization, 12 meses de optimización y 3 de prueba) y, recién ahí, probar los filtros opcionales.
6. **Incubación en Sim101** con Telegram y el vigilante, hasta que la validación en vivo diga VENTAJA CONFIRMADA (unas 60 operaciones si la ventaja existe).
7. Solo entonces, **1 contrato real**. Subir de a un contrato solo con hitos: 150–250 operaciones con el límite inferior del rango por encima de 70%.
