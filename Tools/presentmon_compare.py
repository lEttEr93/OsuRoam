#!/usr/bin/env python3
"""把多次 PresentMon 采集放在一起对比：「提交间隔 vs 显示间隔」。

用法：
    python presentmon_compare.py pm-preview-pace0.csv pm-preview-pace1.csv ...

只关心一件事：**显示间隔**是否均匀（p95 是否≈1 个刷新周期）。
提交间隔作为对照列，用来确认提交侧没有退化。
"""

import csv
import sys
from collections import defaultdict


def collect(path):
    with open(path, newline="", encoding="utf-8-sig") as handle:
        reader = csv.DictReader(handle)
        header = reader.fieldnames or []
        if not header:
            return None

        def pick(*names):
            for name in names:
                if name in header:
                    return name
            return None

        pid = pick("ProcessID", "ProcessId")
        swap = pick("SwapChainAddress", "SwapChain")
        submit = pick("MsBetweenPresents")
        display = pick("MsBetweenDisplayChange")
        mode = pick("PresentMode")
        if not submit:
            return None

        groups = defaultdict(lambda: {"submit": [], "display": [], "mode": defaultdict(int)})
        for row in reader:
            key = (row.get(pid, "?"), row.get(swap, "?") if swap else "?")
            bucket = groups[key]
            try:
                bucket["submit"].append(float(row[submit]))
            except (KeyError, TypeError, ValueError):
                pass
            if display:
                try:
                    value = float(row[display])
                    if value > 0:
                        bucket["display"].append(value)
                except (KeyError, TypeError, ValueError):
                    pass
            if mode:
                bucket["mode"][row.get(mode, "?")] += 1

    if not groups:
        return None

    # 预览的交换链是帧数最多的那条（WPF 主窗的交换链帧数少得多）。
    key = max(groups, key=lambda k: len(groups[k]["submit"]))
    return groups[key]


def pct(values, q):
    if not values:
        return None
    ordered = sorted(values)
    return ordered[min(len(ordered) - 1, int(len(ordered) * q))]


def row(label, stats):
    if stats is None or not stats["submit"]:
        return f"{label:<28}（没有数据）"
    submit = stats["submit"]
    display = stats["display"]
    mode = max(stats["mode"], key=stats["mode"].get) if stats["mode"] else "?"

    def cells(values):
        if not values:
            return "  ——    ——    ——     ——"
        slow = sum(1 for v in values if v > 20)
        return (f"{pct(values, 0.50):5.2f} {pct(values, 0.95):5.2f} "
                f"{pct(values, 0.99):5.2f}  {slow:>3}/{len(values):<4}")

    return (f"{label:<28} {cells(submit)}   {cells(display)}   {mode}")


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2

    print("采集                         提交 p50  p95   p99  >20ms/帧      "
          "显示 p50  p95   p99  >20ms/帧      PresentMode")
    print("-" * 118)
    for index, path in enumerate(sys.argv[1:]):
        label = path.rsplit("\\", 1)[-1].rsplit("/", 1)[-1]
        label = label.replace("pm-preview-pace", "pace=").replace(".csv", "")
        if label.startswith("pace=") is False:
            label = f"#{index} {label}"
        print(row(label, collect(path)))

    print()
    print("看的是「显示 p95」：≈4.2ms（一个刷新周期）表示每帧都准时上屏；")
    print("≈8.4ms 表示约 5% 的画面被多留了一个周期；越接近 4.2 越好。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
