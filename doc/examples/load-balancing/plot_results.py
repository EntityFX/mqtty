"""Render saved results without rerunning or changing the simulation (requires matplotlib)."""
import argparse
import csv
import hashlib
import json
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt


def verify(directory):
    registry = json.loads((directory / "sha256.json").read_text(encoding="utf-8"))
    for name, expected in registry.items():
        path = (directory / name).resolve()
        if not path.is_relative_to(directory.resolve()) or hashlib.sha256(path.read_bytes()).hexdigest() != expected:
            raise ValueError(f"Integrity check failed: {name}")


def seal(directory):
    files = sorted(p for p in directory.rglob("*") if p.is_file()
                   and p != directory / "sha256.json" and p.name != ".experiment")
    (directory / "sha256.json").write_text(json.dumps({
        p.relative_to(directory).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
        for p in files}, indent=2), encoding="utf-8")


def plot_run(directory):
    verify(directory)
    with (directory / "backend-metrics.csv").open(encoding="utf-8") as stream:
        rows = list(csv.DictReader(stream))
    fig, axes = plt.subplots(1, 2, figsize=(10, 3.5), constrained_layout=True)
    for ax, key, label in zip(axes, ["publishers", "uniqueGoodput"], ["Publishers", "Unique messages / load second"]):
        ax.bar([r["backend"] for r in rows], [float(r[key]) for r in rows])
        ax.set(xlabel="Backend", ylabel=label)
    fig.savefig(directory / "backends.svg")
    plt.close(fig)
    seal(directory)


def plot_matrix(directory):
    verify(directory)
    matrix = json.loads((directory / "matrix-summary.json").read_text(encoding="utf-8"))
    records = []
    for item in matrix["results"]:
        run = directory / item["directory"]
        summary = json.loads((run / "summary.json").read_text(encoding="utf-8"))
        records.append(dict(item, **{key: summary[key] for key in ["configuredOfferedRps", "apiAttempts", "attemptDeliveryLoss", "backendCompletedRps", "uniqueGoodput", "publishP99Ms", "receiverP99Ms"]}))
        plot_run(run)
    keys = ["brokerType", "mode", "publishQos", "seed", "status", "configuredOfferedRps", "apiAttempts", "attemptDeliveryLoss", "backendCompletedRps", "uniqueGoodput", "publishP99Ms", "receiverP99Ms"]
    with (directory / "comparison.csv").open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=keys, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(records)
    modes = ["single", "directBalanced", "roundRobin", "leastConnections"]
    for broker in dict.fromkeys(r["brokerType"] for r in records):
        fig, axes = plt.subplots(3, 3, figsize=(13, 10), constrained_layout=True)
        for qos in range(3):
            group = [r for r in records if r["brokerType"] == broker and r["publishQos"] == qos and r["status"] == "completed"]
            for ax, metric in zip(axes[qos], ["backendCompletedRps", "uniqueGoodput", "publishP99Ms"]):
                samples = [[r[metric] for r in group if r["mode"] == mode and r[metric] is not None] for mode in modes]
                means = [sum(s) / len(s) if s else 0 for s in samples]
                ax.bar(modes, means)
                for i, values in enumerate(samples):
                    ax.scatter([i] * len(values), values, color="black", s=12)
                ax.set_title(f"QoS {qos}: {metric}")
                ax.tick_params(axis="x", rotation=30)
                if not any(samples):
                    ax.text(.5, .5, "notApplicable", transform=ax.transAxes, ha="center")
        fig.suptitle(f"{broker}: fixed transport; points = seeds, bars = arithmetic mean")
        fig.savefig(directory / f"{broker}-comparison.svg")
        plt.close(fig)
    seal(directory)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("result_directory", type=Path)
    path = parser.parse_args().result_directory.resolve()
    if (path / "matrix-summary.json").exists():
        plot_matrix(path)
    else:
        plot_run(path)
