#!/usr/bin/env python3
"""Large-topology validation against the fitted message models."""
from __future__ import annotations

import csv
import json
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
PROJECT = ROOT / "src/EntityFX.MqttSimulator/EntityFX.MqttY.Benchmark.Compare/EntityFX.MqttY.Benchmark.Compare.csproj"
OUT = Path(__file__).with_name("large-topologies.csv")

BASE = ["dotnet", "run", "--project", str(PROJECT), "-c", "Release", "--no-build", "--",
        "--repeat", "1", "--warmup-runs", "1", "--measure", "1", "--drain", "10",
        "--no-export", "--no-strict"]


def run(engine: str, brokers: int, publishers: int, nets: int, rps: int,
        topology: str = "star", host: str = "strong", workers: int = 12) -> dict:
    with tempfile.NamedTemporaryFile(suffix=".json", delete=False) as tmp:
        json_path = tmp.name
    args = BASE + ["--engines", engine, "--workers", str(workers),
                   "--brokers", str(brokers), "--publishers", str(publishers),
                   "--nets", str(nets), "--rps", str(rps), "--fed-rps", str(rps),
                   "--fed-topology", topology, "--json", json_path]
    try:
        subprocess.run(args, cwd=ROOT, check=True, capture_output=True, text=True)
        report = json.loads(Path(json_path).read_text())
        engine_report = report["Engines"][0]
        sample = [s for s in engine_report["Samples"] if s["Mode"] == "par"][0]
        core = json.loads(sample["Metrics"]["Core"])
        return {
            "host": host,
            "engine": engine,
            "topology": topology if engine == "fed" else ("star" if engine == "relay" else "n/a"),
            "brokers": brokers,
            "publishers": publishers,
            "nets_per_backend": nets,
            "rps": rps,
            "scheduled": core["scheduled"],
            "delivered": core["unique"],
            "net_in": sample.get("NetworkInbound", 0),
            "wall_s": sample["Wall"],
            "refresh_s": sample["Refresh"],
            "wall_x": engine_report["WallSpeedup"],
            "refresh_x": engine_report["RefreshSpeedup"],
            "status": core["status"],
        }
    finally:
        Path(json_path).unlink(missing_ok=True)


def main() -> None:
    rows: list[dict] = []

    def add(**kwargs):
        rows.append(run(**kwargs))

    # Large LB: 64 backends, 257 networks, up to 2112 clients.
    for publishers, nets in ((256, 4), (1024, 4), (2048, 4), (1024, 1)):
        add(engine="lb", brokers=64, publishers=publishers, nets=nets, rps=20000)

    # Large Federation: B=16 (maximum), up to 2368 clients.
    for publishers in (256, 1024, 2048):
        add(engine="fed", brokers=16, publishers=publishers, nets=1, rps=2000,
            topology="star")
    add(engine="fed", brokers=16, publishers=1024, nets=1, rps=2000, topology="ring")
    add(engine="fed", brokers=16, publishers=1024, nets=1, rps=2000, topology="line")

    # Large Relay: B=16, 1024 publishers.
    add(engine="relay", brokers=16, publishers=1024, nets=1, rps=2000)

    # Saturation probe at the validation boundary.
    add(engine="fed", brokers=16, publishers=1024, nets=1, rps=5000, topology="star")

    with OUT.open("w", newline="") as fh:
        writer = csv.DictWriter(fh, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)
    failures = [r for r in rows if r["status"] != "completed"]
    print(f"wrote {len(rows)} rows to {OUT}; failures={len(failures)}")
    for row in failures:
        print(row)


if __name__ == "__main__":
    main()
