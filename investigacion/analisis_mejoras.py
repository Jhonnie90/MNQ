"""Prueba las mejoras de la investigación sobre la configuración actual del bot."""
import warnings; warnings.filterwarnings("ignore")
from datetime import time
import pandas as pd
from motor import Config, cargar, agrupar, simular, resumen
d5 = cargar("data/mnq_5m.csv", 5); d1 = cargar("data/mnq_1m.csv", 1); s1 = agrupar(d1, 5)
corte = pd.Timestamp("2026-09-24", tz="America/Bogota")
B = dict(inicio=time(8, 35), fin=time(10, 30), filtro_vwap=True, stop_ticks=120, profit_ticks=60, max_ops_dia=3,
         limite_dia_usd=120, limite_ganancia_usd=0, max_atr_vs_stop=2.0)
variantes = {
    "actual": {},
    "salida 1 vela": {"salida_tiempo_velas": 1}, "salida 2 velas": {"salida_tiempo_velas": 2},
    "salida 3 velas": {"salida_tiempo_velas": 3}, "salida 6 velas": {"salida_tiempo_velas": 6},
    "salida 2 velas si pierde": {"salida_tiempo_velas": 2, "salida_tiempo_min_ticks": 0},
    "salida 3 velas si pierde": {"salida_tiempo_velas": 3, "salida_tiempo_min_ticks": 0},
    "breakeven 40/+2": {"breakeven_ticks": 40}, "breakeven 30/+2": {"breakeven_ticks": 30},
    "stop 90": {"stop_ticks": 90}, "stop 100": {"stop_ticks": 100},
    "primera vela alcista": {"filtro_primera_vela": True},
    "gap al alza": {"filtro_gap": True},
    "sobre apertura 9:30": {"filtro_sobre_apertura": True},
    "sobre cierre anterior": {"filtro_sobre_cierre_anterior": True},
}
filas = []
for n, v in variantes.items():
    a = simular(d5, Config(**{**B, **v}, modo_relleno="conservador")); ra = resumen(a)
    b = simular(s1, Config(**{**B, **v}), d1); b = b[b.entrada_t >= corte] if not b.empty else b; rb = resumen(b)
    filas.append({"variante": n, "ops 50d": ra["ops"], "aciertos% 50d": ra["aciertos%"], "neto$ 50d": ra["neto$"], "PF 50d": ra["PF"],
                  "esp$/op": ra["esp$/op"], "maxDD$": ra["maxDD$"], "ops val": rb["ops"], "aciertos% val": rb["aciertos%"], "neto$ val": rb["neto$"],
                  "salidas": a.motivo.value_counts().to_dict() if not a.empty else {}})
pd.set_option("display.width", 250); pd.set_option("display.max_colwidth", 60)
print(pd.DataFrame(filas).to_string(index=False))
