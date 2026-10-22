"""Velas de 1 minuto de los últimos ~30 días (Yahoo solo da 8 días por pedido)."""
import warnings; warnings.filterwarnings("ignore")
from datetime import datetime, timedelta, timezone
import os
import pandas as pd
import yfinance as yf

fin = datetime.now(timezone.utc)
partes = []
for k in range(4):
    hasta = fin - timedelta(days=7 * k)
    desde = hasta - timedelta(days=7)
    df = yf.download("MNQ=F", interval="1m", start=desde, end=hasta, progress=False, auto_adjust=False)
    if isinstance(df.columns, pd.MultiIndex):
        df.columns = df.columns.get_level_values(0)
    partes.append(df[["Open", "High", "Low", "Close", "Volume"]].dropna())
os.makedirs("data", exist_ok=True)
df = pd.concat(partes).sort_index()
df = df[~df.index.duplicated()]
df.index = df.index.tz_convert("America/Bogota")
df.to_csv("data/mnq_1m.csv")
print(len(df), df.index.min(), "->", df.index.max(), "dias:", df.index.normalize().nunique())
