#!/usr/bin/env python3
"""PresentMon CSV → 逐交换链的「提交节奏 / 实际显示节奏」摘要。

用法：
    python presentmon_analyze.py pm-preview.csv [--json]

设计要点（来自专家意见）：
- 不假定字段一定存在：按实际表头挑字段，缺的明确标成不可用；
- 按 **进程 + 交换链地址** 分组，避免把 WPF 主窗、信息窗、预览覆盖层的交换链混在一起；
- 分别统计「提交间隔」和「实际显示间隔」，绝不用前者代替后者；
- Dropped 只作参考：应用提交快于屏幕时，被替换的提交是正常现象。
"""

import csv
import json
import sys
from collections import defaultdict


def pick(header, *names):
    """返回第一个存在的字段名（PresentMon 各版本表头不完全一样）。"""
    for name in names:
        if name in header:
            return name
    return None


def percentile(values, q):
    if not values:
        return None
    ordered = sorted(values)
    index = min(len(ordered) - 1, int(len(ordered) * q))
    return ordered[index]


def summarize(values):
    if not values:
        return None
    return {
        "n": len(values),
        "p50": round(percentile(values, 0.50), 3),
        "p90": round(percentile(values, 0.90), 3),
        "p95": round(percentile(values, 0.95), 3),
        "p99": round(percentile(values, 0.99), 3),
        "max": round(max(values), 3),
        "over_20ms": sum(1 for v in values if v > 20),
    }


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2

    path = sys.argv[1]
    as_json = "--json" in sys.argv[2:]

    with open(path, newline="", encoding="utf-8-sig") as handle:
        reader = csv.DictReader(handle)
        header = reader.fieldnames or []
        if not header:
            print("CSV 表头为空：这个文件不是 PresentMon 输出。")
            return 2

        field_pid = pick(header, "ProcessID", "ProcessId")
        field_swap = pick(header, "SwapChainAddress", "SwapChain")
        field_submit = pick(header, "MsBetweenPresents", "msBetweenPresents")
        field_display = pick(header, "MsBetweenDisplayChange", "msBetweenDisplayChange")
        field_mode = pick(header, "PresentMode", "presentMode")
        field_sync = pick(header, "SyncInterval")
        field_dropped = pick(header, "Dropped", "WasDropped")
        field_app = pick(header, "Application", "ProcessName")
        field_api = pick(header, "Runtime")

        missing = [
            label
            for label, field in (
                ("提交间隔 MsBetweenPresents", field_submit),
                ("显示间隔 MsBetweenDisplayChange", field_display),
                ("PresentMode", field_mode),
                ("Dropped", field_dropped),
            )
            if field is None
        ]

        groups = defaultdict(lambda: {
            "submit": [], "display": [], "modes": defaultdict(int),
            "sync": defaultdict(int), "dropped": 0, "rows": 0, "app": "", "api": "",
        })

        for row in reader:
            key = (row.get(field_pid, "?"), row.get(field_swap, "?") if field_swap else "?")
            bucket = groups[key]
            bucket["rows"] += 1
            if field_app:
                bucket["app"] = row.get(field_app, bucket["app"])
            if field_api:
                bucket["api"] = row.get(field_api, bucket["api"])

            if field_submit:
                try:
                    bucket["submit"].append(float(row[field_submit]))
                except (KeyError, TypeError, ValueError):
                    pass
            if field_display:
                try:
                    value = float(row[field_display])
                    # -1 / 0 表示这一帧没有对应的显示事件，不能当样本。
                    if value > 0:
                        bucket["display"].append(value)
                except (KeyError, TypeError, ValueError):
                    pass
            if field_mode:
                bucket["modes"][row.get(field_mode, "?")] += 1
            if field_sync:
                bucket["sync"][row.get(field_sync, "?")] += 1
            if field_dropped and row.get(field_dropped, "0") not in ("0", "", "false", "False"):
                bucket["dropped"] += 1

    report = {
        "file": path,
        "header_present": header,
        "fields_missing": missing,
        "groups": [],
    }

    for (pid, swap), bucket in sorted(groups.items(), key=lambda kv: -kv[1]["rows"]):
        entry = {
            "process_id": pid,
            "swap_chain": swap,
            "application": bucket["app"],
            "runtime": bucket["api"],
            "rows": bucket["rows"],
            "present_modes": dict(bucket["modes"]),
            "sync_intervals": dict(bucket["sync"]),
            "dropped_rows": bucket["dropped"] or None,
            "submit_interval_ms": summarize(bucket["submit"]),
            "display_interval_ms": summarize(bucket["display"]),
        }
        if field_display is None:
            entry["display_interval_ms"] = "不可用：这个 PresentMon 版本没有 MsBetweenDisplayChange"
        report["groups"].append(entry)

    if as_json:
        print(json.dumps(report, ensure_ascii=False, indent=2))
        return 0

    if missing:
        print("注意：以下字段在这个 CSV 里不存在，相关结论不可用 → " + "；".join(missing))
        print()

    for entry in report["groups"]:
        print(f"进程 {entry['application'] or '?'} (PID {entry['process_id']})｜交换链 {entry['swap_chain']}"
              f"｜{entry['rows']} 帧｜Runtime={entry['runtime'] or '?'}")
        print(f"  PresentMode 分布：{entry['present_modes']}｜SyncInterval：{entry['sync_intervals']}")
        submit = entry["submit_interval_ms"]
        if isinstance(submit, dict):
            print(f"  提交间隔 ms：p50={submit['p50']} p95={submit['p95']} p99={submit['p99']} "
                  f"max={submit['max']}｜>20ms {submit['over_20ms']}/{submit['n']}")
        display = entry["display_interval_ms"]
        if isinstance(display, dict):
            print(f"  显示间隔 ms：p50={display['p50']} p95={display['p95']} p99={display['p99']} "
                  f"max={display['max']}｜>20ms {display['over_20ms']}/{display['n']}")
        else:
            print(f"  显示间隔：{display}")
        if entry["dropped_rows"]:
            print(f"  未显示/被替换的提交：{entry['dropped_rows']}（提交快于刷新时属正常，需结合显示间隔看）")
        print()

    print("判读：提交间隔均匀但显示间隔不均匀 → 问题在显示链路/合成；两者都均匀 → 感知上的卡顿另有原因。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
