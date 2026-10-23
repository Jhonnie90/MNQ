"""Busca configuraciones que suban aciertos sin perder plata.
Elige en ago-12 a sep-23 (velas 5m, relleno conservador) y valida en sep-24 a oct-22 (fills de 1m)."""
import warnings; warnings.filterwarnings("ignore")
import itertools
from datetime import time
import pandas as pd
from motor import Config, cargar, agrupar, simular, resumen
pd.set_option("display.width", 250); pd.set_option("display.max_columns", 30); pd.set_option("display.max_colwidth", 60)

d5 = cargar("data/mnq_5m.csv", 5)
d1 = cargar("data/mnq_1m.csv", 1)
corte = pd.Timestamp("2026-09-24 00:00", tz="America/Bogota")
ins = d5[d5.index < corte]
# Para validar, las señales se calculan con 5m desde antes (calentamiento de EMAs).
sig_val, fino_val = agrupar(d1, 5), d1

ventanas = {"7:30-9:00": (time(7, 30), time(9, 0)), "8:30-10:00": (time(8, 30), time(10, 0)), "8:35-10:30": (time(8, 35), time(10, 30))}
filtros = {"ninguno": {}, "EMA50": {"filtro_tendencia": 50}, "EMA200": {"filtro_tendencia": 200},
           "VWAP": {"filtro_vwap": True}, "pendiente21": {"filtro_pendiente_lenta": True}}
salidas = {f"fijo {s}/{p}t": {"stop_ticks": s, "profit_ticks": p} for s, p in [(16, 24), (40, 40), (60, 60), (80, 80), (80, 40), (120, 60), (60, 90)]}
salidas.update({f"ATR {s}/{p}": {"atr_periodo": 14, "stop_atr": s, "profit_atr": p} for s, p in [(1, 1), (1.5, 1), (1.5, 1.5), (2, 1), (1, 1.5)]})
lados = {"solo largos": {}, "largos+cortos": {"cortos": True}}

filas = []
for (vn, (ini, fin)), (fn, fk), (sn, sk), (ln, lk) in itertools.product(ventanas.items(), filtros.items(), salidas.items(), lados.items()):
    cfg = Config(inicio=ini, fin=fin, limite_dia_usd=1e9, modo_relleno="conservador", **fk, **sk, **lk)
    r = resumen(simular(ins, cfg))
    filas.append({"ventana": vn, "filtro": fn, "salida": sn, "lados": ln, **r, "cfg": cfg})
res = pd.DataFrame(filas)
res["ambig%"] = (100 * res["ambig"] / res["ops"].clip(lower=1)).round(0)
res.to_pickle("data/busqueda_ins.pkl")
cols = ["ventana", "filtro", "salida", "lados", "ops", "aciertos%", "neto$", "PF", "esp$/op", "maxDD$", "ambig%"]
print("Configs:", len(res), "| con PF>1 y >=15 ops:", ((res.PF > 1) & (res.ops >= 15)).sum())
cand = res[(res.PF > 1.0) & (res.ops >= 15)].sort_values(["aciertos%", "PF"], ascending=False)
print("\nTOP 15 en muestra (ordenado por aciertos, exigiendo PF>1):")
print(cand[cols].head(15).to_string(index=False))

print("\nVALIDACIÓN fuera de muestra (sep-24 a oct-22, fills 1m) de esos 15 + original:")
val = []
orig = Config(limite_dia_usd=1e9)
for _, row in pd.concat([cand.head(15), res[(res.ventana == "7:30-9:00") & (res.filtro == "ninguno") & (res.salida == "fijo 16/24t") & (res.lados == "solo largos")]]).iterrows():
    t = simular(sig_val, row["cfg"].__class__(**{**row["cfg"].__dict__, "modo_relleno": "ninja"}), fino_val)
    t = t[t["entrada_t"] >= corte] if not t.empty else t
    rv = resumen(t)
    val.append({"ventana": row.ventana, "filtro": row.filtro, "salida": row.salida, "lados": row.lados,
                "ins_aciertos%": row["aciertos%"], "ins_PF": row.PF,
                "val_ops": rv["ops"], "val_aciertos%": rv["aciertos%"], "val_neto$": rv["neto$"], "val_PF": rv["PF"], "val_ambig": rv["ambig"]})
print(pd.DataFrame(val).to_string(index=False))
