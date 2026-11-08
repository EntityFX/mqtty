#!/usr/bin/env python3
"""Fit and plot the topology/message-count models from measurements.csv."""
from __future__ import annotations

import csv
import json
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
ROWS = list(csv.DictReader((HERE / "measurements.csv").open()))
for row in ROWS:
    for key in ("brokers", "publishers", "nets_per_backend", "rps", "scheduled",
                "attempts", "delivered", "net_in", "net_out", "clients", "networks"):
        row[key] = int(row[key])
    for key in ("wall_s", "refresh_s"):
        row[key] = float(row[key])


def ols(x: np.ndarray, y: np.ndarray) -> tuple[np.ndarray, float, float]:
    coef, *_ = np.linalg.lstsq(x, y, rcond=None)
    pred = x @ coef
    ss_res = float(np.sum((y - pred) ** 2))
    ss_tot = float(np.sum((y - np.mean(y)) ** 2))
    r2 = 1.0 - ss_res / ss_tot if ss_tot > 0 else 1.0
    rmse = float(np.sqrt(np.mean((y - pred) ** 2)))
    return coef, r2, rmse


def rank(x: np.ndarray) -> np.ndarray:
    order = np.argsort(x, kind="mergesort")
    ranks = np.empty_like(order, dtype=float)
    ranks[order] = np.arange(len(x), dtype=float)
    return ranks


def corr(a: np.ndarray, b: np.ndarray) -> tuple[float, float]:
    pearson = float(np.corrcoef(a, b)[0, 1])
    spearman = float(np.corrcoef(rank(a), rank(b))[0, 1])
    return pearson, spearman


def fit_lb(rows: list[dict]) -> dict:
    # X = a*M*L + b*M + c*B + d*P + e
    x = np.array([[r["scheduled"] * r["nets_per_backend"], r["scheduled"],
                   r["brokers"], r["publishers"], 1.0] for r in rows])
    y = np.array([r["net_in"] for r in rows], dtype=float)
    coef, r2, rmse = ols(x, y)
    return {"coef": [float(v) for v in coef], "r2": r2, "rmse": rmse}


def fit_fed(rows: list[dict]) -> dict:
    # X/M = a*B^2 + b*B + c
    usable = [r for r in rows if r["delivered"] == r["scheduled"] * r["brokers"]]
    x = np.array([[r["brokers"] ** 2, r["brokers"], 1.0] for r in usable])
    y = np.array([r["net_in"] / r["scheduled"] for r in usable], dtype=float)
    coef, r2, rmse = ols(x, y)
    return {"coef": [float(v) for v in coef], "r2": r2, "rmse": rmse,
            "rows": len(usable), "excluded": len(rows) - len(usable)}


def normalized_correlation_table(rows: list[dict], names: list[str],
                                 values: list[np.ndarray]) -> list[dict]:
    table = []
    for name, value in zip(names, values):
        row = {"variable": name}
        for name2, value2 in zip(names, values):
            row[name2] = round(corr(value, value2)[0], 3)
        table.append(row)
    return table


def correlation_table(rows: list[dict]) -> list[dict]:
    b = np.log10(np.array([r["brokers"] for r in rows], dtype=float))
    p = np.log10(np.array([r["publishers"] for r in rows], dtype=float))
    c = np.log10(np.array([r["clients"] for r in rows], dtype=float))
    n = np.log10(np.array([r["networks"] for r in rows], dtype=float))
    m = np.log10(np.array([r["scheduled"] for r in rows], dtype=float))
    d = np.log10(np.array([r["delivered"] for r in rows], dtype=float))
    x = np.log10(np.array([r["net_in"] for r in rows], dtype=float))
    names = ["log B", "log P", "log C", "log N", "log M", "log D", "log X"]
    values = [b, p, c, n, m, d, x]
    table = []
    for name, value in zip(names, values):
        row = {"variable": name}
        for name2, value2 in zip(names, values):
            row[name2] = round(corr(value, value2)[0], 3)
        table.append(row)
    return table


def svg_scatter(path: Path, points: list[tuple[float, float]], fit: tuple[float, float],
                xlabel: str, ylabel: str, title: str) -> None:
    width, height = 760, 480
    left, right, top, bottom = 80, 730, 60, 400
    xs = [p[0] for p in points]
    ys = [p[1] for p in points]
    xmin, xmax = min(xs), max(xs)
    ymin, ymax = min(ys), max(ys)
    xpad = (xmax - xmin) * .08 or 1
    ypad = (ymax - ymin) * .08 or 1
    xmin, xmax = xmin - xpad, xmax + xpad
    ymin, ymax = ymin - ypad, ymax + ypad

    def px(x): return left + (x - xmin) / (xmax - xmin) * (right - left)
    def py(y): return bottom - (y - ymin) / (ymax - ymin) * (bottom - top)

    parts = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}">',
             '<rect width="100%" height="100%" fill="#fff"/>',
             f'<text x="{left}" y="32" font-family="sans-serif" font-size="18" font-weight="700">{title}</text>',
             f'<line x1="{left}" y1="{bottom}" x2="{right}" y2="{bottom}" stroke="#334155"/>',
             f'<line x1="{left}" y1="{top}" x2="{left}" y2="{bottom}" stroke="#334155"/>']
    for x in np.linspace(xmin, xmax, 6):
        parts.append(f'<text x="{px(x):.1f}" y="{bottom + 20}" font-family="sans-serif" font-size="11" text-anchor="middle">{x:.2f}</text>')
    for y in np.linspace(ymin, ymax, 6):
        parts.append(f'<text x="{left - 8}" y="{py(y) + 4:.1f}" font-family="sans-serif" font-size="11" text-anchor="end">{y:.1f}</text>')
    for x, y in points:
        parts.append(f'<circle cx="{px(x):.1f}" cy="{py(y):.1f}" r="5" fill="#2563eb" opacity=".85"/>')
    a, b = fit
    parts.append(f'<line x1="{px(xmin):.1f}" y1="{py(a * xmin + b):.1f}" x2="{px(xmax):.1f}" y2="{py(a * xmax + b):.1f}" stroke="#dc2626" stroke-width="2"/>')
    parts.append(f'<text x="{(left + right) / 2}" y="{height - 18}" font-family="sans-serif" font-size="13" text-anchor="middle">{xlabel}</text>')
    parts.append(f'<text x="20" y="{(top + bottom) / 2}" font-family="sans-serif" font-size="13" text-anchor="middle" transform="rotate(-90 20 {(top + bottom) / 2})">{ylabel}</text>')
    parts.append("</svg>")
    path.write_text("\n".join(parts))


def holdout_lb(rows: list[dict]) -> dict:
    # Train X = a*M*L + b*M + c*B + d*P + e on L in {1,2}; test on L=4.
    train = [r for r in rows if r["nets_per_backend"] < 4]
    test = [r for r in rows if r["nets_per_backend"] == 4]
    def design(rs):
        return np.array([[r["scheduled"] * r["nets_per_backend"], r["scheduled"],
                          r["brokers"], r["publishers"], 1.0] for r in rs])
    y = np.array([r["net_in"] for r in train], dtype=float)
    coef, *_ = np.linalg.lstsq(design(train), y, rcond=None)
    pred = float(np.mean(design(test) @ coef))
    actual = float(np.mean([r["net_in"] for r in test]))
    return {"train_rows": len(train), "test_rows": len(test),
            "predicted": pred, "actual": actual,
            "relative_error": abs(pred - actual) / actual}


def svg_series(path: Path, series: list[tuple[str, list[tuple[float, float]], tuple[float, ...], str]],
               xlabel: str, ylabel: str, title: str) -> None:
    width, height = 820, 500
    left, right, top, bottom = 80, 760, 70, 400
    xs = [x for _, pts, _, _ in series for x, _ in pts]
    ys = [y for _, pts, _, _ in series for _, y in pts]
    xmin, xmax = min(xs), max(xs)
    ymin, ymax = min(ys), max(ys)
    ypad = (ymax - ymin) * .12 or 1
    ymin, ymax = max(0, ymin - ypad), ymax + ypad

    def px(x): return left + (x - xmin) / (xmax - xmin) * (right - left)
    def py(y): return bottom - (y - ymin) / (ymax - ymin) * (bottom - top)

    parts = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}">',
             '<rect width="100%" height="100%" fill="#fff"/>',
             f'<text x="{left}" y="34" font-family="sans-serif" font-size="18" font-weight="700">{title}</text>',
             f'<line x1="{left}" y1="{bottom}" x2="{right}" y2="{bottom}" stroke="#334155"/>',
             f'<line x1="{left}" y1="{top}" x2="{left}" y2="{bottom}" stroke="#334155"/>']
    for x in range(int(xmin), int(xmax) + 1):
        if x % 2 == 0:
            parts.append(f'<text x="{px(x):.1f}" y="{bottom + 20}" font-family="sans-serif" font-size="11" text-anchor="middle">{x}</text>')
    for v in range(0, int(ymax) + 1, max(1, int(ymax / 5))):
        parts.append(f'<text x="{left - 8}" y="{py(v) + 4:.1f}" font-family="sans-serif" font-size="11" text-anchor="end">{v}</text>')
        parts.append(f'<line x1="{left}" y1="{py(v):.1f}" x2="{right}" y2="{py(v):.1f}" stroke="#e2e8f0"/>')
    for name, pts, coef, color in series:
        degree = len(coef) - 1
        fit_x = np.linspace(xmin, xmax, 100)
        fit_y = sum(coef[k] * fit_x ** (degree - k) for k in range(degree + 1))
        parts.append(f'<polyline points="{" ".join(f"{px(x):.1f},{py(y):.1f}" for x, y in zip(fit_x, fit_y))}" fill="none" stroke="{color}" stroke-width="2"/>')
        for x, y in pts:
            parts.append(f'<circle cx="{px(x):.1f}" cy="{py(y):.1f}" r="5" fill="{color}" opacity=".85"/>')
    for i, (name, _, _, color) in enumerate(series):
        lx = left + 10 + i * 150
        parts.append(f'<rect x="{lx}" y="{top + 6}" width="13" height="13" fill="{color}"/>')
        parts.append(f'<text x="{lx + 18}" y="{top + 17}" font-family="sans-serif" font-size="12">{name}</text>')
    parts.append(f'<text x="{(left + right) / 2}" y="{height - 18}" font-family="sans-serif" font-size="13" text-anchor="middle">{xlabel}</text>')
    parts.append(f'<text x="22" y="{(top + bottom) / 2}" font-family="sans-serif" font-size="13" text-anchor="middle" transform="rotate(-90 22 {(top + bottom) / 2})">{ylabel}</text>')
    parts.append("</svg>")
    path.write_text("\n".join(parts))


def main() -> None:
    lb = [r for r in ROWS if r["engine"] == "lb"]
    fed_all = [r for r in ROWS if r["engine"] == "fed" and r["topology"] == "star"]
    # Deduplicate the grid overlap and exclude the saturated R=5000 points
    # from the star fit; keep the full set only for the raw plot.
    seen = set()
    fed = []
    for row in fed_all:
        if row["delivered"] != row["scheduled"] * row["brokers"]:
            continue
        key = (row["brokers"], row["publishers"], row["rps"])
        if key in seen:
            continue
        seen.add(key)
        fed.append(row)
    ring = [r for r in ROWS if r["engine"] == "fed" and r["topology"] == "ring"]
    line = [r for r in ROWS if r["engine"] == "fed" and r["topology"] == "line"]

    lb_fit = fit_lb(lb)
    fed_fit = fit_fed(fed)
    relay = [r for r in ROWS if r["engine"] == "relay"]
    relay_x = np.array([[r["brokers"], 1.0] for r in relay])
    relay_y = np.array([r["net_in"] / r["scheduled"] for r in relay])
    relay_coef, relay_r2, relay_rmse = ols(relay_x, relay_y)

    def fit_topology(rows: list[dict], degree: int) -> dict:
        x = np.array([[r["brokers"] ** k for k in range(degree, -1, -1)] for r in rows])
        y = np.array([r["net_in"] / r["scheduled"] for r in rows])
        coef, r2, rmse = ols(x, y)
        return {"coef": [float(v) for v in coef], "r2": r2, "rmse": rmse}

    def holdout(rows: list[dict], degree: int) -> dict:
        train = [r for r in rows if r["brokers"] < 16]
        test = [r for r in rows if r["brokers"] == 16]
        x = np.array([[r["brokers"] ** k for k in range(degree, -1, -1)] for r in train])
        y = np.array([r["net_in"] / r["scheduled"] for r in train])
        coef, _, _ = ols(x, y)
        pred = sum(coef[k] * 16 ** (degree - k) for k in range(degree + 1))
        actual = np.mean([r["net_in"] / r["scheduled"] for r in test])
        return {"train_rows": len(train), "test_rows": len(test),
                "predicted": float(pred), "actual": float(actual),
                "relative_error": float(abs(pred - actual) / actual)}

    topologies = {
        "star": {"degree": 2, **fit_topology(fed, 2), "holdout": holdout(fed, 2)},
        "ring": {"degree": 1, **fit_topology(ring, 1), "holdout": holdout(ring, 1)},
        "line": {"degree": 1, **fit_topology(line, 1), "holdout": holdout(line, 1)},
    }
    lb_holdout = holdout_lb(lb)

    # LB: X/M = a*L + b (setup folded into b); Fed: X/M = a*B^2+b*B+c
    lb_norm_x = np.array([[r["nets_per_backend"], 1.0] for r in lb])
    lb_norm_y = np.array([r["net_in"] / r["scheduled"] for r in lb])
    lb_norm_coef, lb_norm_r2, lb_norm_rmse = ols(lb_norm_x, lb_norm_y)

    results = {
        "rows": len(ROWS),
        "lb": {
            "fit_X_ML_M_B_1": lb_fit,
            "fit_X_per_M_vs_L": {"coef": [float(v) for v in lb_norm_coef],
                                 "r2": lb_norm_r2, "rmse": lb_norm_rmse},
            "delivered_equals_scheduled": all(r["delivered"] == r["scheduled"] for r in lb),
            "correlations": correlation_table(lb),
            "normalized_correlations": normalized_correlation_table(lb,
                ["log X/M", "log L", "log B", "log N", "log C"],
                [np.log10(np.array([r["net_in"] / r["scheduled"] for r in lb])),
                 np.log10(np.array([r["nets_per_backend"] for r in lb], dtype=float)),
                 np.log10(np.array([r["brokers"] for r in lb], dtype=float)),
                 np.log10(np.array([r["networks"] for r in lb], dtype=float)),
                 np.log10(np.array([r["clients"] for r in lb], dtype=float))]),
        },
        "topologies": topologies,
        "lb_holdout": lb_holdout,
        "relay": {
            "fit_X_per_M_vs_B": {"coef": [float(v) for v in relay_coef],
                                  "r2": relay_r2, "rmse": relay_rmse, "rows": len(relay)},
            "delivered_equals_MB": all(r["delivered"] == r["scheduled"] * r["brokers"] for r in relay),
            "normalized_correlations": normalized_correlation_table(relay,
                ["log X/M", "log B", "log N", "log C", "log P"],
                [np.log10(np.array([r["net_in"] / r["scheduled"] for r in relay])),
                 np.log10(np.array([r["brokers"] for r in relay], dtype=float)),
                 np.log10(np.array([r["networks"] for r in relay], dtype=float)),
                 np.log10(np.array([r["clients"] for r in relay], dtype=float)),
                 np.log10(np.array([r["publishers"] for r in relay], dtype=float))]),
        },
        "fed": {
            "fit_X_per_M_vs_B": fed_fit,
            "delivered_equals_MB_unsaturated": all(
                r["delivered"] == r["scheduled"] * r["brokers"] for r in fed
                if r["delivered"] == r["scheduled"] * r["brokers"]),
            "delivered_ratio_min": min(r["delivered"] / (r["scheduled"] * r["brokers"]) for r in fed),
            "delivered_ratio_max": max(r["delivered"] / (r["scheduled"] * r["brokers"]) for r in fed),
            "correlations": correlation_table(fed),
            "normalized_correlations": normalized_correlation_table(fed,
                ["log X/M", "log B", "log N", "log C", "log P"],
                [np.log10(np.array([r["net_in"] / r["scheduled"] for r in fed])),
                 np.log10(np.array([r["brokers"] for r in fed], dtype=float)),
                 np.log10(np.array([r["networks"] for r in fed], dtype=float)),
                 np.log10(np.array([r["clients"] for r in fed], dtype=float)),
                 np.log10(np.array([r["publishers"] for r in fed], dtype=float))]),
        },
    }
    (HERE / "results.json").write_text(json.dumps(results, indent=2))

    # Figures
    (HERE / "figures").mkdir(exist_ok=True)
    svg_scatter(
        HERE / "figures" / "lb-net-per-message-vs-hops.svg",
        [(r["nets_per_backend"], r["net_in"] / r["scheduled"]) for r in lb],
        (float(lb_norm_coef[0]), float(lb_norm_coef[1])),
        "L = число hop-сетей на backend", "X / M, сетевых пакетов на сообщение",
        "LB: сетевые пакеты линейны по длине пути")
    svg_scatter(
        HERE / "figures" / "fed-net-per-message-vs-brokers.svg",
        [(r["brokers"], r["net_in"] / r["scheduled"]) for r in fed
         if r["delivered"] == r["scheduled"] * r["brokers"]],
        (float(fed_fit["coef"][1]), float(fed_fit["coef"][2])),
        "B = число брокеров (star)", "X / M, сетевых пакетов на сообщение",
        "Federation: квадратичный рост по брокерам")
    svg_series(
        HERE / "figures" / "fed-topologies-net-per-message.svg",
        [("star", [(r["brokers"], r["net_in"] / r["scheduled"]) for r in fed],
          tuple(topologies["star"]["coef"]), "#dc2626"),
         ("ring", [(r["brokers"], r["net_in"] / r["scheduled"]) for r in ring],
          tuple(topologies["ring"]["coef"]), "#2563eb"),
         ("line", [(r["brokers"], r["net_in"] / r["scheduled"]) for r in line],
          tuple(topologies["line"]["coef"]), "#16a34a")],
        "B = число брокеров", "X / M, сетевых пакетов на сообщение",
        "Federation: star, ring и line")
    svg_scatter(
        HERE / "figures" / "relay-net-per-message-vs-brokers.svg",
        [(r["brokers"], r["net_in"] / r["scheduled"]) for r in relay],
        (float(relay_coef[0]), float(relay_coef[1])),
        "B = число брокеров (star relay)", "X / M, сетевых пакетов на сообщение",
        "Relay: линейный рост по брокерам")
    svg_scatter(
        HERE / "figures" / "fed-delivered-vs-scheduled.svg",
        [(r["scheduled"] * r["brokers"], r["delivered"]) for r in fed],
        (1.0, 0.0), "M · B = ожидаемые доставки", "D = уникальные доставки",
        "Federation: D = M · B")

    print(json.dumps(results, indent=2))


if __name__ == "__main__":
    main()
