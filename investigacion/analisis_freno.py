"""¿Cada cuánto salta el freno por pura casualidad, y qué tan rápido detecta que la ventaja se perdió?"""
import numpy as np
rng = np.random.default_rng(7)
SIMS, N = 20000, 250
G, P = 28.76, -61.74

def ventana(res, n, minimo):
    """Primer índice donde los aciertos de las últimas n caen bajo minimo (o -1)."""
    c = np.cumsum(np.r_[0, res.astype(int)])
    for i in range(n, len(res) + 1):
        if 100.0 * (c[i] - c[i - n]) / n < minimo:
            return i
    return -1

def cusum(res, h, p_buena=0.79):
    """CUSUM sobre pérdida respecto de lo esperado con ventaja: salta si la caída acumulada supera h dólares."""
    esperado = p_buena * G + (1 - p_buena) * P
    s = 0.0
    for i, g in enumerate(res):
        s = max(0.0, s + esperado - (G if g else P))
        if s > h:
            return i + 1
    return -1

def sprt(res, p0=0.68, p1=0.79, a=0.05, b=0.20):
    A, B = np.log((1 - b) / a), np.log(b / (1 - a))
    w, l = np.log(p1 / p0), np.log((1 - p1) / (1 - p0))
    s = 0.0
    for i, g in enumerate(res):
        s += w if g else l
        if s >= A: return ("acepta", i + 1)
        if s <= B: return ("rechaza", i + 1)
    return ("sigue", len(res))

reglas = {"20 ops < 68% (actual)": lambda r: ventana(r, 20, 68), "50 ops < 64%": lambda r: ventana(r, 50, 64),
          "50 ops < 62%": lambda r: ventana(r, 50, 62), "40 ops < 60%": lambda r: ventana(r, 40, 60),
          "CUSUM $400": lambda r: cusum(r, 400), "CUSUM $500": lambda r: cusum(r, 500), "CUSUM $600": lambda r: cusum(r, 600)}
print(f"{'regla':<24}{'falsa alarma (79% real, 250 ops)':>34}{'detecta si cae a 65% (ops, mediana)':>40}")
for nombre, f in reglas.items():
    fa = np.mean([f(rng.random(N) < 0.79) > 0 for _ in range(3000)])
    det = [f(rng.random(N) < 0.65) for _ in range(3000)]
    det_ok = [d for d in det if d > 0]
    print(f"{nombre:<24}{100*fa:>32.1f}%{np.median(det_ok):>30.0f}  (detecta {100*len(det_ok)/len(det):.0f}%)")

print("\nSPRT 68% vs 79% (alfa 5%, beta 20%):")
for p in (0.79, 0.75, 0.72, 0.68, 0.65):
    r = [sprt(rng.random(600) < p) for _ in range(5000)]
    acc = np.mean([x[0] == "acepta" for x in r]); rej = np.mean([x[0] == "rechaza" for x in r])
    print(f"  aciertos reales {int(p*100)}%: confirma ventaja {100*acc:.0f}% | la descarta {100*rej:.0f}% | ops medianas hasta decidir {np.median([x[1] for x in r]):.0f}")
