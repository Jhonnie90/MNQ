"""Nulo 'mejor de N': si las entradas fueran al azar, ¿qué tan bueno sale el MEJOR de las 360 configuraciones?
Repite la búsqueda de busqueda.py (mismo periodo, mismos criterios) con señales aleatorias."""
import warnings; warnings.filterwarnings("ignore")
import itertools, sys
from datetime import time
import numpy as np, pandas as pd
from motor import Config, cargar, simular, resumen

d5 = cargar("data/mnq_5m.csv", 5)
ins = d5[d5.index < pd.Timestamp("2026-09-24", tz="America/Bogota")]
ventanas = [(time(7, 30), time(9, 0)), (time(8, 30), time(10, 0)), (time(8, 35), time(10, 30))]
filtros = [{}, {"filtro_tendencia": 50}, {"filtro_tendencia": 200}, {"filtro_vwap": True}, {"filtro_pendiente_lenta": True}]
salidas = [{"stop_ticks": s, "profit_ticks": p} for s, p in [(16, 24), (40, 40), (60, 60), (80, 80), (80, 40), (120, 60), (60, 90)]]
salidas += [{"atr_periodo": 14, "stop_atr": s, "profit_atr": p} for s, p in [(1, 1), (1.5, 1), (1.5, 1.5), (2, 1), (1, 1.5)]]
lados = [{}, {"cortos": True}]
semillas = int(sys.argv[1]) if len(sys.argv) > 1 else 30
mejores = []
for k in range(semillas):
    mejor = 0.0
    for j, ((ini, fin), fk, sk, lk) in enumerate(itertools.product(ventanas, filtros, salidas, lados)):
        cfg = Config(inicio=ini, fin=fin, limite_dia_usd=1e9, modo_relleno="conservador", prob_aleatoria=0.05,
                     semilla=k * 1000 + j, **fk, **sk, **lk)
        r = resumen(simular(ins, cfg))
        if r["ops"] >= 15 and r["PF"] > 1.0:
            mejor = max(mejor, r["aciertos%"])
    mejores.append(mejor)
    print(f"semilla {k}: mejor aciertos con PF>1 y >=15 ops = {mejor:.1f}%", flush=True)
m = pd.Series(mejores)
print(f"\nMejor-de-360 al azar: mediana {m.median():.1f}% | p90 {m.quantile(.9):.1f}% | máx {m.max():.1f}%")
print(f"Bot real en la misma búsqueda: 75.0% -> semillas al azar con mejor >= 75%: {100*(m >= 75).mean():.0f}%")
