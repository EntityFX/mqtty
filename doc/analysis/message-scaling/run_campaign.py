#!/usr/bin/env python3
"""Run the topology/message-count campaign for the scaling article.

Each configuration is one `Benchmark.Compare` invocation in parallel mode.
Message counts are read from the JSON report: Scheduled (Steps), Delivered
(Unique) and network Inbound/Outbound packets.
"""
from __future__ import annotations

import csv
import json
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
PROJECT = ROOT / "src/EntityFX.MqttSimulator/EntityFX.MqttY.Benchmark.Compare/EntityFX.MqttY.Benchmark.Compare.csproj"
OUT = Path(__file__).with_name("measurements.csv")

BASE = ["dotnet", "run", "--project", str(PROJECT), "-c", "Release", "--no-build", "--",
        "--mode", "par", "--repeat", "1", "--warmup-runs", "1", "--measure", "1",
        "--drain", "10", "--workers", "4", "--no-export", "--no-strict"]


def run(engine: str, brokers: int, publishers: int, nets: int, rps: int,
        topology: str | None = None) -> dict:
    with tempfile.NamedTemporaryFile(suffix=".json", delete=False) as tmp:
        json_path = tmp.name
    args = BASE + ["--engines", engine, "--brokers", str(brokers),
                   "--publishers", str(publishers), "--nets", str(nets),
                   "--rps", str(rps), "--fed-rps", str(rps), "--json", json_path]
    if topology is not None:
        args += ["--fed-topology", topology]
    try:
        subprocess.run(args, cwd=ROOT, check=True, capture_output=True, text=True)
        report = json.loads(Path(json_path).read_text())
        sample = report["Engines"][0]["Samples"][0]
        core = json.loads(sample["Metrics"]["Core"])
        return {
            "engine": engine,
            "topology": topology or "n/a",
            "brokers": brokers,
            "publishers": publishers,
            "nets_per_backend": nets,
            "rps": rps,
            "scheduled": core["scheduled"],
            "attempts": core["attempts"],
            "delivered": core["unique"],
            "net_in": sample.get("NetworkInbound", 0),
            "net_out": sample.get("NetworkOutbound", 0),
            "wall_s": sample["Wall"],
            "refresh_s": sample["Refresh"],
            "status": core["status"],
        }
    finally:
        Path(json_path).unlink(missing_ok=True)


def main() -> None:
    rows: list[dict] = []

    # LB: vary one factor at a time around the reference point.
    def lb(b, p, n, r):
        row = run("lb", b, p, n, r, "n/a")
        row["clients"] = p + b
        row["networks"] = 1 + b * n
        rows.append(row)

    for b in (2, 4, 8, 16):
        lb(b, 32, 2, 2000)
    for p in (8, 16, 32, 64):
        lb(4, p, 2, 2000)
    for n in (1, 2, 4):
        lb(8, 32, n, 2000)
    for r in (500, 1000, 2000, 5000):
        lb(8, 32, 2, r)
    for b, p, n, r in ((2, 8, 1, 1000), (4, 16, 4, 5000),
                       (16, 64, 4, 1000), (8, 64, 1, 5000)):
        lb(b, p, n, r)

    # Federation: star topology has B sites + one transit network.
    def fed(b, p, r, topology="star"):
        row = run("fed", b, p, 1, r, topology)
        if topology == "star":
            row["clients"] = p + b + b * (b + 1)
            row["networks"] = b + 1
        elif topology == "ring":
            row["clients"] = p + 5 * b
            row["networks"] = b
        else:  # line
            row["clients"] = p + 5 * b - 2
            row["networks"] = b
        rows.append(row)

    for b in (2, 4, 8, 16):
        fed(b, max(32, b), 1000)
    for p in (8, 16, 32, 64):
        fed(4, max(p, 4), 1000)
    for r in (500, 1000, 2000, 5000):
        fed(8, 32, r)
    for b, p, r in ((2, 8, 2000), (4, 16, 5000), (8, 64, 500), (16, 64, 2000)):
        fed(b, max(p, b), r)

    # Three Federation topologies at fixed P/R.
    for topology in ("star", "ring", "line"):
        for b in (2, 4, 8, 16):
            fed(b, max(32, b), 1000, topology)

    # Relay: B sites, one transit network; each site runs 3B-1 relay clients.
    def relay(b, p, r):
        row = run("relay", b, max(p, b), 1, r, "star")
        row["clients"] = p + 3 * b * b
        row["networks"] = b + 1
        rows.append(row)

    for b in (2, 4, 8, 16):
        relay(b, max(32, b), 1000)
    for p in (8, 16, 32, 64):
        relay(4, max(p, 4), 1000)
    for r in (500, 1000, 2000, 5000):
        relay(8, 32, r)
    for b, p, r in ((2, 8, 2000), (4, 16, 5000), (8, 64, 500), (16, 64, 2000)):
        relay(b, max(p, b), r)

    with OUT.open("w", newline="") as fh:
        writer = csv.DictWriter(fh, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)

    failed = [r for r in rows if r["status"] != "completed"]
    print(f"wrote {len(rows)} rows to {OUT}; failures={len(failed)}")
    if failed:
        for row in failed:
            print(row, file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
