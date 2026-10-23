"""Compara operación por operación el backtest de NinjaTrader con la réplica en Python.

Si las dos no hacen las mismas operaciones, los números de la investigación no son los del bot real
(diferencias de horario, de sesión del VWAP, de datos o de llenados).

1. En NinjaTrader: Strategy Analyzer -> backtest de MNQ 5 min -> pestaña Trades -> clic derecho -> Export (CSV).
2. Bajar los datos del mismo periodo (descargar.py) o exportar las velas desde NinjaTrader.
3. python cotejar.py trades_ninjatrader.csv [--zona America/Bogota] [--datos data/mnq_5m.csv]

--zona es la zona horaria configurada en NinjaTrader (Tools > Options > General), en la que vienen las horas del export.
"""
import argparse
import sys
import warnings
from datetime import time

import pandas as pd

from motor import Config, cargar, simular

warnings.filterwarnings("ignore")


def columna(df, *nombres):
    for c in df.columns:
        if c.strip().lower() in nombres:
            return c
    sys.exit(f"No encuentro ninguna de {nombres} en el export. Columnas: {list(df.columns)}")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("export")
    ap.add_argument("--zona", default="America/Bogota")
    ap.add_argument("--datos", default="data/mnq_5m.csv")
    ap.add_argument("--minutos", type=int, default=5, help="tamaño de vela de los datos")
    ap.add_argument("--tolerancia", type=int, default=6, help="minutos de diferencia aceptados en la hora de entrada")
    a = ap.parse_args()

    nt = pd.read_csv(a.export, sep=None, engine="python")
    c_ent = columna(nt, "entry time", "hora de entrada", "entrada")
    c_px = columna(nt, "entry price", "precio de entrada")
    c_sal = columna(nt, "exit name", "nombre de salida", "salida")
    nt["entrada"] = pd.to_datetime(nt[c_ent]).dt.tz_localize(a.zona).dt.tz_convert("America/Bogota")
    nt["precio"] = nt[c_px].astype(str).str.replace(r"[^\d.]", "", regex=True).astype(float)

    df = cargar(a.datos, a.minutos)
    desde, hasta = nt["entrada"].min().normalize(), nt["entrada"].max().normalize() + pd.Timedelta(days=1)
    cfg = Config(inicio=time(8, 35), fin=time(10, 30), filtro_vwap=True, stop_ticks=120, profit_ticks=60, max_ops_dia=3,
                 limite_dia_usd=120, limite_ganancia_usd=0, max_atr_vs_stop=2.0, modo_relleno="conservador")
    py = simular(df, cfg)
    # En Python la hora es la del cierre de la vela de entrada; NinjaTrader marca la apertura.
    py["entrada"] = pd.to_datetime(py["entrada_t"]) - pd.Timedelta(minutes=a.minutos)
    py = py[(py["entrada"] >= desde) & (py["entrada"] < hasta)]

    tol = pd.Timedelta(minutes=a.tolerancia)
    usados, filas = set(), []
    for _, t in nt.iterrows():
        cand = py[(py["entrada"] - t["entrada"]).abs() <= tol]
        cand = cand[~cand.index.isin(usados)]
        if cand.empty:
            filas.append(("solo NinjaTrader", t["entrada"], t["precio"], None, t[c_sal], None))
            continue
        p = cand.iloc[0]
        usados.add(cand.index[0])
        filas.append(("en los dos", t["entrada"], t["precio"], p["precio_entrada"], t[c_sal], p["motivo"]))
    for i, p in py[~py.index.isin(usados)].iterrows():
        filas.append(("solo Python", p["entrada"], None, p["precio_entrada"], None, p["motivo"]))

    r = pd.DataFrame(filas, columns=["donde", "entrada", "precio NT", "precio Python", "salida NT", "salida Python"]).sort_values("entrada")
    r["entrada"] = r["entrada"].dt.strftime("%Y-%m-%d %H:%M")
    r["dif ticks"] = ((r["precio NT"] - r["precio Python"]) / 0.25).round(0)
    pd.set_option("display.width", 200)
    print(r.to_string(index=False))
    ambos = (r["donde"] == "en los dos").sum()
    print(f"\nNinjaTrader: {len(nt)} | Python: {len(py)} | coinciden: {ambos} | solo NT: {(r['donde'] == 'solo NinjaTrader').sum()}"
          f" | solo Python: {(r['donde'] == 'solo Python').sum()}")
    if ambos:
        print(f"Diferencia de precio de entrada (mediana): {r['dif ticks'].abs().median():.0f} ticks")


if __name__ == "__main__":
    main()
