import csv
import json
import os
import subprocess
import sys

import os
# .../doc/analysis/message-scaling -> repository root
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
BIN = os.path.join(ROOT, 'src/EntityFX.MqttSimulator/EntityFX.MqttY.Benchmark.Compare/bin/Release/net6.0/EntityFX.MqttY.Benchmark.Compare')
OUT = os.path.join(ROOT, 'doc/analysis/message-scaling/compute-scaling.csv')
TMP = '/tmp/.private/entityfx/opencode/scale'
os.makedirs(TMP, exist_ok=True)
env = dict(os.environ, TMPDIR='/tmp/.private/entityfx/opencode')

rows = []


def run(engine, topology, brokers, publishers, nets, rps, label):
    json_path = os.path.join(TMP, f'{label}.json')
    args = [BIN, '--engines', engine, '--mode', 'seq', '--repeat', '1',
            '--warmup-runs', '0', '--measure', '1', '--drain', '2',
            '--workers', '12', '--brokers', str(brokers),
            '--publishers', str(publishers), '--nets', str(nets),
            '--rps', str(rps), '--fed-rps', str(rps),
            '--fed-topology', topology, '--no-export', '--json', json_path]
    try:
        subprocess.run(args, cwd=ROOT, env=env, stdout=subprocess.DEVNULL,
                       stderr=subprocess.DEVNULL, timeout=300, check=False)
        data = json.load(open(json_path))
        e = data['Engines'][0]
        sample = e['Samples'][0]
        rows.append({
            'series': label.split('-')[0],
            'engine': engine,
            'topology': topology,
            'brokers': brokers,
            'publishers': publishers,
            'nets_per_backend': nets,
            'rps': rps,
            'steps': sample['Steps'],
            'wall_s': round(sample['Wall'], 6),
            'refresh_s': round(sample['Refresh'], 6),
            'net_in': sample['NetworkInbound'],
            'net_out': sample['NetworkOutbound'],
            'status': sample['Status'],
        })
        print(f"{label:<24} steps={sample['Steps']:>10} wall={sample['Wall']:.3f}s", flush=True)
    except Exception as exc:
        print(f"{label:<24} FAILED {exc}", flush=True)


# A. brokers, per engine
for engine in ('lb', 'relay', 'fed'):
    for brokers in (2, 4, 8, 16):
        run(engine, 'star', brokers, 32, 2, 2000, f'brokers-{engine}-b{brokers}')

# B. publishers, per engine
for engine in ('lb', 'relay', 'fed'):
    for publishers in (8, 16, 32, 64, 128):
        run(engine, 'star', 8, publishers, 2, 2000, f'publishers-{engine}-p{publishers}')

# C. nets per backend (LB)
for nets in (1, 2, 4, 8):
    run('lb', 'star', 8, 32, nets, 2000, f'nets-lb-n{nets}')

# D. topologies (federation)
for topology in ('star', 'ring', 'line'):
    run('fed', topology, 8, 32, 2, 2000, f'topology-fed-{topology}')

# E. rate, per engine
for engine in ('lb', 'relay'):
    for rps in (500, 1000, 2000, 5000):
        run(engine, 'star', 8, 32, 2, rps, f'rate-{engine}-r{rps}')

with open(OUT, 'w', newline='') as f:
    writer = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
    writer.writeheader()
    writer.writerows(rows)
print(f'written {len(rows)} rows to {OUT}')
