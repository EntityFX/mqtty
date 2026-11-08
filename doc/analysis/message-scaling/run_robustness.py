#!/usr/bin/env python3
"""QoS and seed robustness checks for the message model."""
from __future__ import annotations

import csv
import json
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
PROJECT = ROOT / "src/EntityFX.MqttSimulator/EntityFX.MqttY.Benchmark.Compare/EntityFX.MqttY.Benchmark.Compare.csproj"
OUT = Path(__file__).with_name("robustness.csv")
BASE = ["dotnet", "run", "--project", str(PROJECT), "-c", "Release", "--no-build", "--",
        "--mode", "par", "--repeat", "1", "--warmup-runs", "1", "--measure", "1",
        "--drain", "10", "--workers", "4", "--no-export", "--no-strict"]


def run(engine: str, seed: int, qos: int) -> dict:
    with tempfile.NamedTemporaryFile(suffix=".json", delete=False) as tmp:
        path = tmp.name
    if engine == "lb":
        args = BASE + ["--engines", "lb", "--brokers", "64", "--nets", "4",
                       "--publishers", "256", "--rps", "5000"]
    else:
        args = BASE + ["--engines", engine, "--brokers", "16", "--publishers", "256",
                       "--fed-rps", "1000", "--fed-topology", "star"]
    args += ["--seed", str(seed), "--qos", str(qos), "--json", path]
    try:
        subprocess.run(args, cwd=ROOT, check=True, capture_output=True, text=True)
        report = json.loads(Path(path).read_text())
        sample = [s for s in report["Engines"][0]["Samples"] if s["Mode"] == "par"][0]
        core = json.loads(sample["Metrics"]["Core"])
        return {"engine": engine, "qos": qos, "seed": seed,
                "scheduled": core["scheduled"], "delivered": core["unique"],
                "net_in": sample.get("NetworkInbound", 0),
                "status": core["status"]}
    finally:
        Path(path).unlink(missing_ok=True)


def main() -> None:
    rows = []
    for engine in ("lb", "fed"):
        for qos in (0, 1, 2):
            for seed in (1, 7, 42):
                rows.append(run(engine, seed, qos))
    with OUT.open("w", newline="") as fh:
        writer = csv.DictWriter(fh, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)
    print(f"wrote {len(rows)} rows to {OUT}; failures={sum(r['status'] != 'completed' for r in rows)}")


if __name__ == "__main__":
    main()
