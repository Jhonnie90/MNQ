"""Descarga velas intradía de MNQ (contrato continuo de Yahoo) en hora de Bogotá."""
import sys
import os
import pandas as pd
import yfinance as yf

def bajar(intervalo, periodo):
    df = yf.download("MNQ=F", interval=intervalo, period=periodo, progress=False, auto_adjust=False)
    if isinstance(df.columns, pd.MultiIndex):
        df.columns = df.columns.get_level_values(0)
    df = df[["Open", "High", "Low", "Close", "Volume"]].dropna()
    df.index = df.index.tz_convert("America/Bogota")
    return df

if __name__ == "__main__":
    os.makedirs("data", exist_ok=True)
    for intervalo, periodo in [("5m", "60d"), ("2m", "60d"), ("15m", "60d"), ("1m", "8d")]:
        df = bajar(intervalo, periodo)
        df.to_csv(f"data/mnq_{intervalo}.csv")
        print(intervalo, len(df), df.index.min(), "->", df.index.max())
