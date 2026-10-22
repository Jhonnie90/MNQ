"""Configuración por defecto del .cs nuevo vs. el bot original."""
import warnings; warnings.filterwarnings("ignore")
from datetime import time
import pandas as pd
from motor import Config, cargar, agrupar, simular, resumen
pd.set_option("display.width", 220)
d5 = cargar("data/mnq_5m.csv", 5); d1 = cargar("data/mnq_1m.csv", 1); s1 = agrupar(d1, 5)
corte = pd.Timestamp("2026-09-24", tz="America/Bogota")
# Horas en Bogotá: con horario de verano en EE. UU., 8:35-10:30 COT = 9:35-11:30 Nueva York.
nuevo = Config(inicio=time(8, 35), fin=time(10, 30), filtro_vwap=True, stop_ticks=120, profit_ticks=60,
               max_ops_dia=3, limite_dia_usd=120, limite_ganancia_usd=0)
filas = []
for nombre, cfg in [("Original", Config()), ("Nuevo (defaults del .cs)", nuevo)]:
    for datos, fn in [("ago12-oct22, velas 5m, relleno conservador", lambda c: simular(d5, c.__class__(**{**c.__dict__, "modo_relleno": "conservador"}))),
                      ("sep24-oct22, revisado minuto a minuto", lambda c: (lambda t: t[t.entrada_t >= corte] if not t.empty else t)(simular(s1, c, d1)))]:
        t = fn(cfg); r = resumen(t); r.update(bot=nombre, datos=datos)
        if not t.empty:
            r["motivos"] = t.motivo.value_counts().to_dict()
        filas.append(r)
print(pd.DataFrame(filas).set_index(["bot", "datos"]).to_string())
t = simular(d5, nuevo.__class__(**{**nuevo.__dict__, "modo_relleno": "conservador"}))
print("\nOperaciones del nuevo (50 días):")
print(t[["entrada_t", "lado", "precio_entrada", "precio_salida", "motivo", "pnl_usd"]].assign(entrada_t=lambda x: x.entrada_t.astype(str).str[:16]).to_string(index=False))
