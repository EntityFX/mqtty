#!/usr/bin/env python3
"""Enrich large-topology measurements with model predictions and errors."""
from __future__ import annotations

import csv
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
rows = list(csv.DictReader((HERE / "large-topologies.csv").open()))
results = json.loads((HERE / "results.json").read_text())

lb = results["lb"]["fit_X_ML_M_B_1"]["coef"]  # M*L, M, B, P, const
star = results["topologies"]["star"]["coef"]   # B^2, B, const
ring = results["topologies"]["ring"]["coef"]   # B, const
line = results["topologies"]["line"]["coef"]   # B, const
relay = results["relay"]["fit_X_per_M_vs_B"]["coef"]  # B, const


def predict(row: dict) -> float:
    m = int(row["scheduled"])
    b = int(row["brokers"])
    p = int(row["publishers"])
    l = int(row["nets_per_backend"])
    if row["engine"] == "lb":
        return lb[0] * m * l + lb[1] * m + lb[2] * b + lb[3] * p + lb[4]
    if row["engine"] == "relay":
        return m * (relay[0] * b + relay[1])
    if row["topology"] == "star":
        return m * (star[0] * b * b + star[1] * b + star[2])
    if row["topology"] == "ring":
        return m * (ring[0] * b + ring[1])
    return m * (line[0] * b + line[1])


for row in rows:
    b = int(row["brokers"])
    p = int(row["publishers"])
    l = int(row["nets_per_backend"])
    if row["engine"] == "lb":
        clients, networks = p + b, 1 + b * l
    elif row["engine"] == "relay":
        clients, networks = p + 3 * b * b, b + 1
    elif row["topology"] == "star":
        clients, networks = p + b * b + 2 * b, b + 1
    elif row["topology"] == "ring":
        clients, networks = p + 5 * b, b
    else:
        clients, networks = p + 5 * b - 2, b
    predicted = predict(row)
    measured = int(row["net_in"])
    row["clients"] = clients
    row["networks"] = networks
    row["theory_x"] = round(predicted, 1)
    row["error_pct"] = round(abs(predicted - measured) / measured * 100, 3)
    row["delivery_ratio"] = round(int(row["delivered"]) / (int(row["scheduled"]) * b), 3) \
        if row["engine"] != "lb" else 1.0

fields = list(rows[0].keys())
with (HERE / "large-topologies.csv").open("w", newline="") as fh:
    writer = csv.DictWriter(fh, fieldnames=fields)
    writer.writeheader()
    writer.writerows(rows)

print(f"rows={len(rows)} max_error={max(float(r['error_pct']) for r in rows):.3f}%")
