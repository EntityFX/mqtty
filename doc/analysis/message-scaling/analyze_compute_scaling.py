import csv
from collections import defaultdict

import os
PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'compute-scaling.csv')


def linreg(xs, ys):
    n = len(xs)
    mx = sum(xs) / n
    my = sum(ys) / n
    sxx = sum((x - mx) ** 2 for x in xs)
    sxy = sum((x - mx) * (y - my) for x, y in zip(xs, ys))
    slope = sxy / sxx if sxx else 0.0
    intercept = my - slope * mx
    ss_tot = sum((y - my) ** 2 for y in ys)
    ss_res = sum((y - (slope * x + intercept)) ** 2 for x, y in zip(xs, ys))
    return slope, intercept, (1 - ss_res / ss_tot if ss_tot else 1.0)


def quad(xs, ys):
    # y = a x^2 + b x + c via least squares (3x3 normal equations)
    n = len(xs)
    s = [[sum(x ** (i + j) for x in xs) for j in range(3)] for i in range(3)]
    t = [sum((x ** i) * y for x, y in zip(xs, ys)) for i in range(3)]
    # gaussian elimination
    for col in range(3):
        pivot = max(range(col, 3), key=lambda r: abs(s[r][col]))
        s[col], s[pivot] = s[pivot], s[col]
        t[col], t[pivot] = t[pivot], t[col]
        for r in range(col + 1, 3):
            factor = s[r][col] / s[col][col]
            for c in range(col, 3):
                s[r][c] -= factor * s[col][c]
            t[r] -= factor * t[col]
    coef = [0.0] * 3
    for r in range(2, -1, -1):
        coef[r] = (t[r] - sum(s[r][c] * coef[c] for c in range(r + 1, 3))) / s[r][r]
    a, b, c = coef
    my = sum(ys) / n
    ss_tot = sum((y - my) ** 2 for y in ys)
    ss_res = sum((y - (a * x * x + b * x + c)) ** 2 for x, y in zip(xs, ys))
    return a, b, c, (1 - ss_res / ss_tot if ss_tot else 1.0)


rows = list(csv.DictReader(open(PATH)))
groups = defaultdict(list)
for r in rows:
    key = (r['series'], r['engine'], r['topology'])
    groups[key].append(r)

for (series, engine, topology), items in sorted(groups.items()):
    param = None
    for key in ('brokers', 'publishers', 'nets_per_backend', 'rps'):
        values = [int(i[key]) for i in items]
        if len(set(values)) > 1:
            param = key
            break
    print(f'=== {series} / {engine} / {topology} ===')
    if param is None:
        print(f"{'value':<10}{'steps':>8}{'wall_s':>9}{'refresh_s':>11}{'net_in':>10}")
        for i in sorted(items, key=lambda i: i['topology']):
            print(f"{i['topology']:<10}{int(i['steps']):>8}{float(i['wall_s']):>9.4f}"
                  f"{float(i['refresh_s']):>11.4f}{int(i['net_in']):>10}")
        print()
        continue

    items = sorted(items, key=lambda i: int(i[param]))
    print(f"{'value':<7}{'steps':>7}{'wall_s':>9}{'refresh_s':>11}{'net_in':>10}{'us/step':>10}")
    for i in items:
        steps = int(i['steps'])
        wall = float(i['wall_s'])
        print(f"{i[param]:<7}{steps:>7}{wall:>9.4f}{float(i['refresh_s']):>11.4f}"
              f"{int(i['net_in']):>10}{wall * 1e6 / steps:>10.2f}")
    xs = [int(i[param]) for i in items]
    ys = [float(i['wall_s']) for i in items]
    slope, intercept, r2 = linreg(xs, ys)
    a, b, c, r2q = quad(xs, ys)
    print(f"wall ~ {param}: linear slope={slope:.5f}, R2={r2:.3f} | "
          f"quadratic a={a:.6f}, b={b:.5f}, R2={r2q:.3f}")
    print()
