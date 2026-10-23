import warnings; warnings.filterwarnings("ignore")
from datetime import time
import pandas as pd
from motor import Config, cargar, agrupar, simular, resumen, _atr, TICK
d5 = cargar("data/mnq_5m.csv", 5); d1 = cargar("data/mnq_1m.csv", 1); s1 = agrupar(d1, 5)
B = dict(inicio=time(8, 35), fin=time(10, 30), filtro_vwap=True, stop_ticks=120, profit_ticks=60, max_ops_dia=3, limite_dia_usd=120, limite_ganancia_usd=0)
a = _atr(d5, 14) / TICK; h = d5.t_cierre.dt.time
v = a[(h >= time(8, 35)) & (h < time(10, 30))]
print(f"ATR(14) 5m en la ventana, en ticks: mediana {v.median():.0f} | p75 {v.quantile(.75):.0f} | p90 {v.quantile(.9):.0f} | máx {v.max():.0f}")
print(f"ATR/stop(120): mediana {v.median()/120:.2f} | p90 {v.quantile(.9)/120:.2f} | máx {v.max()/120:.2f}\n")
for x in (0, 2.0, 1.75, 1.5, 1.25, 1.0, 0.8):
    r = resumen(simular(d5, Config(**B, max_atr_vs_stop=x, modo_relleno="conservador")))
    print(f"no entrar si ATR > {x:>4} x stop -> ops {r['ops']:>2} | aciertos {r['aciertos%']}% | neto ${r['neto$']} | PF {r['PF']}")
# Prueba en el bot ORIGINAL (stop 16): el filtro debería bloquear casi todo
r = resumen(simular(d5, Config(max_atr_vs_stop=1.5, modo_relleno="conservador")))
print(f"\nBot original (stop 16t) con filtro 1.5: ops {r['ops']} (sin filtro: 29)")
