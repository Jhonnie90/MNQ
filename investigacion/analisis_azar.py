"""¿La señal EMA+VWAP le gana a entrar al azar con las mismas salidas?"""
import warnings; warnings.filterwarnings("ignore")
from datetime import time
import numpy as np, pandas as pd
from motor import Config, cargar, simular, resumen
d5 = cargar("data/mnq_5m.csv", 5)
base = dict(inicio=time(8, 35), fin=time(10, 30), stop_ticks=120, profit_ticks=60, max_ops_dia=3,
            limite_dia_usd=120, limite_ganancia_usd=0, modo_relleno="conservador")
real = resumen(simular(d5, Config(filtro_vwap=True, **base)))
print("Estrategia real:", real)
for nombre, extra in [("azar puro", {}), ("azar sobre VWAP", {"filtro_vwap": True})]:
    filas = []
    for k in range(300):
        r = resumen(simular(d5, Config(prob_aleatoria=0.05, semilla=k, **extra, **base)))
        filas.append(r)
    df = pd.DataFrame(filas)
    w = df["aciertos%"]
    print(f"\n{nombre} (300 simulaciones): ops media {df.ops.mean():.0f} | aciertos media {w.mean():.1f}% "
          f"[p5 {w.quantile(.05):.1f} – p95 {w.quantile(.95):.1f}] | PF mediana {df.PF.median():.2f} | neto mediana ${df['neto$'].median():.0f}")
    print(f"  % de simulaciones al azar con aciertos >= {real['aciertos%']}%: {100*(w >= real['aciertos%']).mean():.1f}%"
          f" | con neto >= ${real['neto$']:.0f}: {100*(df['neto$'] >= real['neto$']).mean():.1f}%")
