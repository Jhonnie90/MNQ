"""¿Alcanzan las operaciones para decir que el bot tiene ventaja?

Lee el registro del bot (Documentos\\NinjaTrader 8\\MNQAutomaticoPrueba_operaciones.csv) o el export de
operaciones del Strategy Analyzer de NinjaTrader, y calcula:
- aciertos y su rango probable (intervalo de Wilson al 95%),
- la probabilidad de ver esos aciertos si en realidad el bot estuviera en el punto de equilibrio,
- la prueba secuencial (SPRT) que usa el bot en vivo, operación por operación,
- cuántas operaciones hacen falta para distinguir el acierto esperado del punto de equilibrio.

Uso:
    python validacion.py ruta.csv [--cuenta Sim101] [--equilibrio 68] [--esperado 79] [--pruebas 360]
"""
import argparse
import math
import re
import sys

import pandas as pd

Z95 = 1.96


def leer_resultados(ruta: str, cuenta: str | None) -> list[float]:
    df = pd.read_csv(ruta, sep=None, engine="python")
    if "GananciaUSD" in df.columns:  # registro del bot
        if cuenta:
            df = df[df["Cuenta"] == cuenta]
        return df["GananciaUSD"].astype(float).tolist()
    col = next((c for c in df.columns if c.strip().lower() in ("profit", "ganancia", "beneficio")), None)
    if col is None:
        sys.exit(f"No encuentro la columna de ganancia en {ruta}. Columnas: {list(df.columns)}")

    def num(x):  # "$28.76", "($61.74)", "-61,74 $"
        s = str(x).strip()
        neg = s.startswith("(") or "-" in s
        s = re.sub(r"[^\d,.]", "", s)
        if s.count(",") and s.count("."):
            s = s.replace(",", "")
        else:
            s = s.replace(",", ".")
        v = float(s) if s else 0.0
        return -v if neg else v
    return [num(x) for x in df[col]]


def wilson(g: int, n: int) -> tuple[float, float]:
    p, z2 = g / n, Z95 ** 2
    centro = (p + z2 / (2 * n)) / (1 + z2 / n)
    margen = Z95 * math.sqrt(p * (1 - p) / n + z2 / (4 * n * n)) / (1 + z2 / n)
    return centro - margen, centro + margen


def p_valor(g: int, n: int, p0: float) -> float:
    """Probabilidad de ver g o más aciertos de n si el acierto real fuera p0 (binomial exacta)."""
    return sum(math.comb(n, k) * p0 ** k * (1 - p0) ** (n - k) for k in range(g, n + 1))


def sprt(resultados: list[bool], p0: float, p1: float, alfa=0.05, beta=0.20) -> tuple[str, int]:
    a, b = math.log((1 - beta) / alfa), math.log(beta / (1 - alfa))
    gana, pierde = math.log(p1 / p0), math.log((1 - p1) / (1 - p0))
    s = 0.0
    for i, r in enumerate(resultados, 1):
        s += gana if r else pierde
        if s >= a:
            return "VENTAJA CONFIRMADA", i
        if s <= b:
            return "SIN VENTAJA (se parece más al punto de equilibrio)", i
    return "falta evidencia, seguir probando", len(resultados)


def n_necesario(p0: float, p1: float, alfa=0.05, potencia=0.80) -> int:
    """Operaciones para detectar p1 frente a p0 con prueba de una cola (aproximación normal)."""
    za = {0.05: 1.645, 0.01: 2.326}.get(alfa) or 1.645
    zb = {0.80: 0.842, 0.90: 1.282}.get(potencia) or 0.842
    n = ((za * math.sqrt(p0 * (1 - p0)) + zb * math.sqrt(p1 * (1 - p1))) / (p1 - p0)) ** 2
    return math.ceil(n)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("csv")
    ap.add_argument("--cuenta", help="solo esta cuenta (registro del bot), p. ej. Sim101")
    ap.add_argument("--equilibrio", type=float, default=68.0, help="punto de equilibrio en %% de aciertos")
    ap.add_argument("--esperado", type=float, default=79.0, help="aciertos que mostró el backtest, en %%")
    ap.add_argument("--pruebas", type=int, default=1, help="configuraciones probadas para elegir esta (corrige por suerte de la selección)")
    a = ap.parse_args()

    ganancias = leer_resultados(a.csv, a.cuenta)
    if not ganancias:
        sys.exit("No hay operaciones.")
    res = [g > 0 for g in ganancias]
    n, g = len(res), sum(res)
    p0, p1 = a.equilibrio / 100, a.esperado / 100
    lo, hi = wilson(g, n)
    pv = p_valor(g, n, p0)
    pv_corr = min(1.0, pv * a.pruebas)
    estado, en = sprt(res, p0, p1)

    print(f"Operaciones: {n} | aciertos: {g} ({100 * g / n:.1f}%) | neto: {sum(ganancias):.2f} USD")
    print(f"Rango probable del acierto real (95%): {100 * lo:.1f}% a {100 * hi:.1f}%")
    print(f"  -> el límite inferior {'SUPERA' if lo > p0 else 'NO supera'} el punto de equilibrio de {a.equilibrio:.0f}%")
    print(f"Si el bot estuviera en el equilibrio, ver {g} o más aciertos de {n} pasaría el {100 * pv:.1f}% de las veces", end="")
    print(f" ({100 * pv_corr:.1f}% corrigiendo por {a.pruebas} configuraciones probadas)" if a.pruebas > 1 else "")
    print(f"Prueba secuencial (SPRT {a.equilibrio:.0f}% vs {a.esperado:.0f}%): {estado} (operación {en})")
    print(f"Para distinguir {a.esperado:.0f}% de {a.equilibrio:.0f}% hacen falta unas {n_necesario(p0, p1)} operaciones"
          f" ({n_necesario(p0, p1, alfa=0.01)} con un criterio más exigente)")


if __name__ == "__main__":
    main()
