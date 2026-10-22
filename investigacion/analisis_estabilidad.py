import warnings; warnings.filterwarnings("ignore")
from datetime import time
import pandas as pd
from motor import Config, cargar, simular, resumen
pd.set_option("display.width", 200)
d5 = cargar("data/mnq_5m.csv", 5)
B = dict(inicio=time(8, 35), fin=time(10, 30), filtro_vwap=True, max_ops_dia=3, limite_dia_usd=120, limite_ganancia_usd=0, modo_relleno="conservador")
def corre(**k):
    c = {**B, "stop_ticks": 120, "profit_ticks": 60}; c.update(k)
    return resumen(simular(d5, Config(**c)))

stops, objs = [60, 80, 100, 120, 140, 160, 200], [30, 40, 50, 60, 80, 100]
W, P = pd.DataFrame(index=stops, columns=objs), pd.DataFrame(index=stops, columns=objs)
for s in stops:
    for o in objs:
        r = corre(stop_ticks=s, profit_ticks=o, limite_dia_usd=max(120, 2 * s * 0.5 + 3))
        W.loc[s, o], P.loc[s, o] = r["aciertos%"], r["PF"]
print("ACIERTOS % (filas = stop en ticks, columnas = objetivo en ticks)"); print(W.to_string())
print("\nFACTOR DE GANANCIA"); print(P.to_string())

print("\nSLIPPAGE (ticks en contra en entrada y stop):")
for sl in range(0, 5):
    r = corre(slippage_ticks=sl); print(f"  {sl} ticks -> aciertos {r['aciertos%']}% | neto ${r['neto$']} | PF {r['PF']}")

print("\nHORARIO (hora Nueva York = Bogotá + 1 en estos meses):")
for ini, fin in [((8, 35), (10, 30)), ((8, 40), (10, 30)), ((8, 45), (10, 30)), ((8, 50), (10, 30)), ((9, 0), (10, 30)), ((8, 35), (10, 0)), ((8, 35), (11, 0))]:
    r = corre(inicio=time(*ini), fin=time(*fin))
    ny = lambda t: f"{t[0]+1}:{t[1]:02d}"
    print(f"  {ny(ini)}-{ny(fin)} NY -> ops {r['ops']} | aciertos {r['aciertos%']}% | neto ${r['neto$']} | PF {r['PF']}")

print("\nEMAs:")
for a, b in [(9, 21), (8, 21), (10, 21), (9, 18), (9, 25), (5, 13), (12, 26)]:
    r = corre(ema_rapida=a, ema_lenta=b); print(f"  {a}/{b} -> ops {r['ops']} | aciertos {r['aciertos%']}% | PF {r['PF']}")
