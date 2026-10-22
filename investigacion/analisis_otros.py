"""Misma lógica en otros micro futuros de índices, con stop/objetivo escalados a su volatilidad."""
import warnings; warnings.filterwarnings("ignore")
from datetime import time
import numpy as np, pandas as pd, yfinance as yf
import motor
from motor import Config, simular, resumen, _atr

INSTR = {"MNQ=F": (0.25, 2.0), "MES=F": (0.25, 5.0), "MYM=F": (1.0, 0.5), "M2K=F": (0.1, 5.0)}
def bajar(sym):
    df = yf.download(sym, interval="5m", period="60d", progress=False, auto_adjust=False)
    if isinstance(df.columns, pd.MultiIndex): df.columns = df.columns.get_level_values(0)
    df = df[["Open", "High", "Low", "Close", "Volume"]].dropna()
    df.index = df.index.tz_convert("America/Bogota"); df = df[~df.index.duplicated()]
    df["t_cierre"] = df.index + pd.Timedelta(minutes=5)
    return df
def atr_ventana(df):
    a = _atr(df, 14); h = df.t_cierre.dt.time
    return a[(h >= time(8, 35)) & (h < time(10, 30))].median()

datos = {s: bajar(s) for s in INSTR}
ref = atr_ventana(datos["MNQ=F"])
filas = []
for s, (tick, usd_pt) in INSTR.items():
    motor.TICK, motor.USD_POR_PUNTO = tick, usd_pt
    df = datos[s]; esc = atr_ventana(df) / ref
    st, ob = max(4, round(30 * esc / tick)), max(2, round(15 * esc / tick))
    riesgo_tick = 0.5 * 2.0 / usd_pt  # para que el límite diario sea ~2 stops
    B = dict(inicio=time(8, 35), fin=time(10, 30), filtro_vwap=True, stop_ticks=st, profit_ticks=ob, max_ops_dia=3,
             limite_dia_usd=2 * st * tick * usd_pt + 1, limite_ganancia_usd=0, modo_relleno="conservador")
    r = resumen(simular(df, Config(**B)))
    az = pd.DataFrame([resumen(simular(df, Config(**B, prob_aleatoria=0.05, semilla=k))) for k in range(150)])
    filas.append({"instr": s, "stop/obj ticks": f"{st}/{ob}", "ops": r["ops"], "aciertos%": r["aciertos%"], "PF": r["PF"], "neto$": r["neto$"],
                  "azar aciertos% (media)": round(az["aciertos%"].mean(), 1), "% azar >= bot": round(100 * (az["aciertos%"] >= r["aciertos%"]).mean(), 1)})
motor.TICK, motor.USD_POR_PUNTO = 0.25, 2.0
print(pd.DataFrame(filas).to_string(index=False))
