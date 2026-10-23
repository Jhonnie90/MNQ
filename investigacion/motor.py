"""Réplica en Python de MNQAutomaticoPrueba (NinjaTrader 8) para medir aciertos.

Imita la semántica de NinjaTrader con Calculate.OnBarClose:
- La hora de una vela es la de su CIERRE (Yahoo la marca con la de apertura).
- Las señales se calculan en velas del gráfico (p. ej. 5 min) y se evalúan a su cierre;
  la orden a mercado se llena en la apertura de la vela siguiente con slippage en contra.
- Stop y objetivo se revisan en velas más finas (1 min) cuando hay datos, como
  OrderFillResolution.High en NinjaTrader. El objetivo (límite) solo se llena si el
  precio lo atraviesa (IsFillLimitOnTouch = false).
- Si stop y objetivo caen en la misma vela fina: "ninja" usa la regla de proximidad de
  la apertura; "conservador" asume que primero tocó el stop.
"""
from dataclasses import dataclass
from datetime import time
import numpy as np
import pandas as pd

TICK = 0.25          # MNQ: 0.25 puntos por tick
USD_POR_PUNTO = 2.0  # MNQ: $2 por punto -> $0.50 por tick
COMISION_RT = 1.24   # comisión ida y vuelta por contrato (aprox. NinjaTrader + CME)


@dataclass(frozen=True)
class Config:
    ema_rapida: int = 9
    ema_lenta: int = 21
    profit_ticks: int = 24
    stop_ticks: int = 16
    inicio: time = time(7, 30)
    fin: time = time(9, 0)
    max_ops_dia: int = 3
    limite_dia_usd: float = 20.0        # bloqueo si P&L del día <= -X (y >= +X si no hay límite de ganancia)
    limite_ganancia_usd: float | None = None  # None = igual a limite_dia_usd; 0 = sin límite
    slippage_ticks: int = 1
    # --- filtros opcionales (apagados = bot original) ---
    largos: bool = True
    cortos: bool = False
    filtro_tendencia: int = 0           # >0: largos solo si Close > EMA(n) (cortos si <)
    filtro_vwap: bool = False           # largos sobre VWAP de la sesión, cortos debajo
    filtro_pendiente_lenta: bool = False  # EMA lenta subiendo para largos (bajando cortos)
    atr_periodo: int = 0                # >0: stop/objetivo en múltiplos de ATR
    stop_atr: float = 1.0
    profit_atr: float = 1.0
    min_ticks: int = 8                  # piso de stop/objetivo cuando se usa ATR
    max_atr_vs_stop: float = 0.0        # >0: no entrar si ATR(atr_periodo o 14) en ticks > X * stop
    modo_relleno: str = "ninja"         # "ninja" | "conservador"
    prob_aleatoria: float = 0.0         # >0: reemplaza el cruce por una entrada al azar con esta probabilidad por vela
    semilla: int = 0
    # --- salidas y filtros de la investigación (apagados por defecto) ---
    salida_tiempo_velas: int = 0        # >0: salir a mercado tras N velas del gráfico sin tocar objetivo
    salida_tiempo_min_ticks: int | None = None  # None = salir siempre; X = salir solo si no va X ticks a favor (como el .cs)
    breakeven_ticks: int = 0            # >0: al cierre de vela, si fue X ticks a favor, stop a entrada + breakeven_mas_ticks
    breakeven_mas_ticks: int = 2
    filtro_primera_vela: bool = False   # largos solo si la vela de 9:30-9:35 NY cerró alcista
    filtro_gap: bool = False            # largos solo si la apertura de 9:30 NY abrió sobre el cierre de 16:00 anterior
    filtro_sobre_apertura: bool = False # largos solo si el precio está sobre la apertura de 9:30 NY
    filtro_sobre_cierre_anterior: bool = False  # largos solo si el precio está sobre el cierre de 16:00 anterior


def cargar(ruta: str, minutos: int) -> pd.DataFrame:
    df = pd.read_csv(ruta, index_col=0)
    df.index = pd.to_datetime(df.index, utc=True).tz_convert("America/Bogota")
    df = df[~df.index.duplicated()].sort_index()
    df["t_cierre"] = df.index + pd.Timedelta(minutes=minutos)
    return df


def agrupar(df1: pd.DataFrame, minutos: int) -> pd.DataFrame:
    """Arma velas de N minutos a partir de velas de 1 minuto."""
    g = df1[["Open", "High", "Low", "Close", "Volume"]].resample(f"{minutos}min", label="left", closed="left")
    df = g.agg({"Open": "first", "High": "max", "Low": "min", "Close": "last", "Volume": "sum"}).dropna()
    df["t_cierre"] = df.index + pd.Timedelta(minutes=minutos)
    return df


def _ema(x: pd.Series, n: int) -> pd.Series:
    return x.ewm(span=n, adjust=False).mean()  # igual que EMA de NinjaTrader


def _atr(df: pd.DataFrame, n: int) -> pd.Series:
    prev = df["Close"].shift(1)
    tr = pd.concat([df["High"] - df["Low"], (df["High"] - prev).abs(), (df["Low"] - prev).abs()], axis=1).max(axis=1)
    return tr.ewm(alpha=1.0 / n, adjust=False).mean()  # Wilder, como el ATR de NinjaTrader


def _vwap_sesion(df: pd.DataFrame) -> pd.Series:
    # La sesión de CME arranca a las 17:00 de Bogotá (con horario de verano en EE. UU.).
    sesion = (df["t_cierre"] + pd.Timedelta(hours=7)).dt.date
    tp = (df["High"] + df["Low"] + df["Close"]) / 3.0
    v = df["Volume"].replace(0, np.nan).fillna(1.0)
    return (tp * v).groupby(sesion).cumsum() / v.groupby(sesion).cumsum()


def _senales(sig: pd.DataFrame, cfg: Config) -> pd.DataFrame:
    """Indicadores y cruces por vela del gráfico, ya evaluados al cierre."""
    e1, e2 = _ema(sig["Close"], cfg.ema_rapida), _ema(sig["Close"], cfg.ema_lenta)
    out = pd.DataFrame(index=sig.index)
    out["t_cierre"] = sig["t_cierre"]
    out["arriba"] = (e1.shift(1) <= e2.shift(1)) & (e1 > e2)
    if cfg.prob_aleatoria > 0:
        out["arriba"] = np.random.default_rng(cfg.semilla).random(len(sig)) < cfg.prob_aleatoria
    out["abajo"] = (e1.shift(1) >= e2.shift(1)) & (e1 < e2)
    ok_l = pd.Series(True, index=sig.index)
    ok_c = pd.Series(True, index=sig.index)
    if cfg.filtro_tendencia:
        t = _ema(sig["Close"], cfg.filtro_tendencia)
        ok_l &= sig["Close"] > t
        ok_c &= sig["Close"] < t
    if cfg.filtro_vwap:
        v = _vwap_sesion(sig)
        ok_l &= sig["Close"] > v
        ok_c &= sig["Close"] < v
    if cfg.filtro_pendiente_lenta:
        d = e2.diff()
        ok_l &= d > 0
        ok_c &= d < 0
    if cfg.max_atr_vs_stop > 0:
        a = _atr(sig, cfg.atr_periodo or 14) / TICK
        stop_t = (np.maximum(cfg.stop_atr * a * TICK, cfg.min_ticks * TICK) / TICK) if cfg.atr_periodo else cfg.stop_ticks
        tranquilo = a <= cfg.max_atr_vs_stop * stop_t
        ok_l &= tranquilo
        ok_c &= tranquilo
    if cfg.filtro_primera_vela or cfg.filtro_gap or cfg.filtro_sobre_apertura or cfg.filtro_sobre_cierre_anterior:
        ny_ini = sig.index.tz_convert("America/New_York")
        ny_fin = sig["t_cierre"].dt.tz_convert("America/New_York")
        fecha = pd.Series(ny_ini.date, index=sig.index)
        hm = pd.Series(ny_ini.hour * 100 + ny_ini.minute, index=sig.index)
        hm_fin = ny_fin.dt.hour * 100 + ny_fin.dt.minute
        apertura = sig["Open"].where(hm == 930).groupby(fecha).transform("max")
        if cfg.filtro_primera_vela:
            alcista = (sig["Close"] > sig["Open"]).astype(float).where(hm == 930).groupby(fecha).transform("max")
            ok_l &= alcista == 1
            ok_c &= alcista == 0
        cierre = sig["Close"].where(hm_fin == 1600).groupby(fecha).max().dropna()
        previo = fecha.map(cierre.reindex(sorted(set(fecha))).ffill().shift(1))
        if cfg.filtro_sobre_cierre_anterior:
            ok_l &= sig["Close"] > previo
            ok_c &= sig["Close"] < previo
        if cfg.filtro_gap:
            ok_l &= apertura > previo
            ok_c &= apertura < previo
        if cfg.filtro_sobre_apertura:
            ok_l &= sig["Close"] > apertura
            ok_c &= sig["Close"] < apertura
    out["ok_l"], out["ok_c"] = ok_l, ok_c
    if cfg.atr_periodo:
        a = _atr(sig, cfg.atr_periodo)
        sd = np.maximum(cfg.stop_atr * a, cfg.min_ticks * TICK)
        od = np.maximum(cfg.profit_atr * a, cfg.min_ticks * TICK)
        out["sd"], out["od"] = (sd / TICK).round() * TICK, (od / TICK).round() * TICK
    else:
        out["sd"], out["od"] = cfg.stop_ticks * TICK, cfg.profit_ticks * TICK
    out["n"] = np.arange(len(sig))
    return out


def simular(sig: pd.DataFrame, cfg: Config, fino: pd.DataFrame | None = None) -> pd.DataFrame:
    """sig: velas del gráfico. fino: velas de 1 min para revisar stop/objetivo (opcional)."""
    fino = sig if fino is None else fino
    s = _senales(sig, cfg)
    # Cada vela fina sabe si en su cierre también cierra una vela del gráfico.
    ev = s.set_index("t_cierre")
    f = fino[["Open", "High", "Low", "Close", "t_cierre"]].copy()
    f = f.join(ev, on="t_cierre", how="left")
    es_cierre = f["n"].notna().to_numpy()
    o, h, l, c = (f[k].to_numpy() for k in ("Open", "High", "Low", "Close"))
    tc = f["t_cierre"].to_numpy()
    horas = f["t_cierre"].dt.time.to_numpy()
    fechas = f["t_cierre"].dt.date.to_numpy()
    arriba, abajo = f["arriba"].to_numpy(), f["abajo"].to_numpy()
    ok_l, ok_c = f["ok_l"].to_numpy(), f["ok_c"].to_numpy()
    sd_v, od_v, n_v = f["sd"].to_numpy(), f["od"].to_numpy(), f["n"].to_numpy()
    slip = cfg.slippage_ticks * TICK

    ops, abierta, pendiente, salida_pendiente, motivo_salida = [], None, None, False, "CierreHorario"
    fecha_cont, ops_hoy, pnl_ini, bloqueo, pnl_acum = None, 0, 0.0, False, 0.0
    ambiguas = 0

    def cerrar(op, i, precio, motivo):
        nonlocal pnl_acum
        op.update(salida_t=tc[i], precio_salida=precio, motivo=motivo,
                  pnl_usd=(precio - op["precio_entrada"]) * op["lado"] * USD_POR_PUNTO - COMISION_RT)
        pnl_acum += op["pnl_usd"]
        ops.append(op)

    for i in range(len(f)):
        if salida_pendiente and abierta is not None:
            cerrar(abierta, i, o[i] - slip * abierta["lado"], motivo_salida)
            abierta = None
        salida_pendiente = False
        if pendiente is not None and abierta is None:
            lado, sd, od = pendiente
            px = o[i] + slip * lado
            abierta = dict(entrada_t=tc[i], lado=lado, precio_entrada=px, stop=px - sd * lado, objetivo=px + od * lado, mae_ticks=0.0, mfe_ticks=0.0, velas=0)
        pendiente = None

        if abierta is not None:
            op, lado = abierta, abierta["lado"]
            fav = (min(h[i], op["objetivo"]) - op["precio_entrada"]) if lado == 1 else (op["precio_entrada"] - max(l[i], op["objetivo"]))
            contra = (op["precio_entrada"] - max(l[i], op["stop"])) if lado == 1 else (min(h[i], op["stop"]) - op["precio_entrada"])
            op["mfe_ticks"] = max(op["mfe_ticks"], fav / TICK)
            op["mae_ticks"] = max(op["mae_ticks"], contra / TICK)
            if lado == 1:
                toca_stop, toca_obj = l[i] <= op["stop"], h[i] >= op["objetivo"] + TICK
            else:
                toca_stop, toca_obj = h[i] >= op["stop"], l[i] <= op["objetivo"] - TICK
            if toca_stop and toca_obj:
                ambiguas += 1
                if cfg.modo_relleno == "conservador":
                    primero = "stop"
                else:
                    abre_cerca_alto = (h[i] - o[i]) < (o[i] - l[i])
                    primero = ("obj" if abre_cerca_alto else "stop") if lado == 1 else ("stop" if abre_cerca_alto else "obj")
                toca_stop, toca_obj = primero == "stop", primero == "obj"
            if toca_stop:
                gap = (o[i] < op["stop"]) if lado == 1 else (o[i] > op["stop"])
                cerrar(op, i, (o[i] if gap else op["stop"]) - slip * lado, "Stop")
                abierta = None
            elif toca_obj:
                cerrar(op, i, op["objetivo"], "Objetivo")
                abierta = None

        if not es_cierre[i]:
            continue
        # OnBarUpdate al cierre de la vela del gráfico.
        if fecha_cont != fechas[i]:
            fecha_cont, ops_hoy, pnl_ini, bloqueo = fechas[i], 0, pnl_acum, False
        hoy = pnl_acum - pnl_ini
        gan = cfg.limite_dia_usd if cfg.limite_ganancia_usd is None else cfg.limite_ganancia_usd
        if (gan > 0 and hoy >= gan) or hoy <= -cfg.limite_dia_usd:
            bloqueo = True
        if n_v[i] < 50:
            continue
        if abierta is not None and horas[i] >= cfg.fin:
            salida_pendiente, motivo_salida = True, "CierreHorario"
            continue
        if abierta is not None:
            op = abierta
            op["velas"] += 1
            if cfg.salida_tiempo_velas and op["velas"] >= cfg.salida_tiempo_velas and (
                    cfg.salida_tiempo_min_ticks is None or (c[i] - op["precio_entrada"]) * op["lado"] / TICK < cfg.salida_tiempo_min_ticks):
                salida_pendiente, motivo_salida = True, "SalidaTiempo"
                continue
            if cfg.breakeven_ticks and op["mfe_ticks"] >= cfg.breakeven_ticks:
                be = op["precio_entrada"] + cfg.breakeven_mas_ticks * TICK * op["lado"]
                op["stop"] = max(op["stop"], be) if op["lado"] == 1 else min(op["stop"], be)
        if abierta is None and cfg.inicio <= horas[i] < cfg.fin and not bloqueo and ops_hoy < cfg.max_ops_dia:
            lado = 0
            if cfg.largos and arriba[i] and ok_l[i]:
                lado = 1
            elif cfg.cortos and abajo[i] and ok_c[i]:
                lado = -1
            if lado:
                ops_hoy += 1
                pendiente = (lado, sd_v[i], od_v[i])

    t = pd.DataFrame(ops)
    t.attrs["ambiguas"] = ambiguas
    return t


def resumen(t: pd.DataFrame) -> dict:
    amb = t.attrs.get("ambiguas", 0)
    if t.empty:
        return {"ops": 0, "aciertos%": np.nan, "neto$": 0.0, "PF": np.nan, "esp$/op": np.nan, "maxDD$": 0.0, "ambig": amb}
    g = t["pnl_usd"]
    ganadas, perdidas = g[g > 0], g[g <= 0]
    curva = g.cumsum()
    pf = ganadas.sum() / -perdidas.sum() if perdidas.sum() < 0 else np.inf
    return {
        "ops": len(t),
        "aciertos%": round(100 * len(ganadas) / len(t), 1),
        "neto$": round(g.sum(), 2),
        "PF": round(pf, 2),
        "esp$/op": round(g.mean(), 2),
        "maxDD$": round((curva - curva.cummax()).min(), 2),
        "ambig": amb,
    }
