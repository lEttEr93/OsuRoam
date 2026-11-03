"""待审 tag 审核：汇总 → 去重 → 聚类 → 生成 AI 归类用的清单。

两种输入：
    python Tools/review-pending-tags.py --file data/pending-tags.json   # 客户端导出的队列
    python Tools/review-pending-tags.py --issues                        # 直接读仓库里 label=tag-pool 的 issue
                                                                        # 需要环境变量 GH_TOKEN（只读即可）

输出（默认写 docs/tag-review/）：
    candidates.json  去重聚类后的候选，按出现次数降序，给 AI 用
    review.md        人看的表：簇、总次数、是否可能是人名/歌名
    prompt.md        可直接粘给任意 LLM 的归类提示词（要求「不是 tag 就返回 null」）

审核完只改 Tools/build-tag-pool.py 的 TABLE，然后重跑它生成 data/tag-pool.json。
"""
import argparse
import json
import os
import re
import sys
import urllib.request
from collections import defaultdict

REPO = "lEttEr93/OsuRoam"
HERE = os.path.dirname(os.path.abspath(__file__))
POOL = os.path.join(HERE, "..", "data", "tag-pool.json")
OUT_DIR = os.path.join(HERE, "..", "docs", "tag-review")

# 复数/连字符的粗归一，用于把 "jacks" 和 "jack" 归到一个簇
NORMALISE = [
    (re.compile(r"[\s_-]+"), " "),
    (re.compile(r"'s$"), ""),
    (re.compile(r"ies$"), "y"),
    (re.compile(r"([a-z])s$"), r"\1"),
]


def normalise(name):
    value = name.strip().lower()
    for pattern, replacement in NORMALISE:
        value = pattern.sub(replacement, value)
    return value


def load_pool():
    with open(POOL, encoding="utf-8") as handle:
        payload = json.load(handle)
    names = {tag for group in payload["groups"] for tag in group["tags"]}
    aliases = {key.lower(): value for key, value in payload.get("aliases", {}).items()}
    return names, aliases


def from_file(path):
    with open(path, encoding="utf-8") as handle:
        payload = json.load(handle)
    if isinstance(payload, dict) and "entries" in payload:
        return {entry["name"]: entry["count"] for entry in payload["entries"]}
    if isinstance(payload, dict) and "tags" in payload:  # 提交载荷
        return {row["tag"]: row["count"] for row in payload["tags"]}
    raise SystemExit(f"看不懂的格式：{path}")


def from_issues():
    token = os.environ.get("GH_TOKEN") or ""
    if not token:
        raise SystemExit("需要 GH_TOKEN 才能读 issue")
    url = f"https://api.github.com/repos/{REPO}/issues?labels=tag-pool&state=open&per_page=100"
    request = urllib.request.Request(url)
    request.add_header("Authorization", f"Bearer {token}")
    request.add_header("Accept", "application/vnd.github+json")
    request.add_header("User-Agent", "osu-roam-review")
    with urllib.request.urlopen(request, timeout=40) as handle:
        issues = json.loads(handle.read().decode())

    counts = defaultdict(int)
    for issue in issues:
        body = issue.get("body") or ""
        for block in re.findall(r"```json(.*?)```", body, re.S):
            try:
                payload = json.loads(block)
            except json.JSONDecodeError:
                continue
            for row in payload.get("tags", []):
                counts[row.get("tag", "").strip().lower()] += int(row.get("count", 1))
    return dict(counts), len(issues)


def cluster(counts):
    buckets = defaultdict(lambda: {"variants": defaultdict(int), "total": 0})
    for name, count in counts.items():
        key = normalise(name)
        buckets[key]["variants"][name] += count
        buckets[key]["total"] += count
    rows = []
    for key, bucket in buckets.items():
        variants = sorted(bucket["variants"].items(), key=lambda kv: -kv[1])
        rows.append({"key": key, "total": bucket["total"], "variants": variants})
    rows.sort(key=lambda row: -row["total"])
    return rows


PROMPT = """你是 osu!mania 谱面 tag 的审核员。下面是玩家谱库里反复出现、但还不在白名单里的 tag。

任务：只挑出**能当筛选维度**的 tag，分成五类：
- genre（曲风/流派，如 artcore、phonk、j-core）
- meta（曲目属性，如 remix、dump、vocal）
- language（演唱语言）
- source（来源/活动，如 bms、mwc、featured artist）
- reject（不是筛选维度：人名、歌名、社团名、难度名、比赛队名、乱码、纯数字等）

规则：
1. **拒绝优先**。osu 的 tag 里绝大多数是作者名和歌名，宁可不收也不要收错。
2. 同一个东西的不同写法合并成一条，给出 canonical（小写）和 aliases。
3. 判断不了就放到 reject，不要猜。
4. 输出严格 JSON：{"accept":[{"name":"...","group":"genre","aliases":["..."]}],"reject":["..."]}

候选（次数 | 变体）：
%s
"""


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--file", help="客户端导出的 pending-tags.json")
    parser.add_argument("--issues", action="store_true", help="读仓库 issue")
    parser.add_argument("--out", default=OUT_DIR)
    args = parser.parse_args()

    names, aliases = load_pool()
    if args.issues:
        counts, issue_count = from_issues()
        print(f"读了 {issue_count} 个 issue")
    elif args.file:
        counts = from_file(args.file)
    else:
        parser.error("要么 --file，要么 --issues")

    known = 0
    filtered = {}
    for name, count in counts.items():
        canonical = aliases.get(name, name)
        if canonical in names:
            known += 1
            continue
        if not name or len(name) < 2 or len(name) > 40:
            continue
        filtered[name] = filtered.get(name, 0) + count

    rows = cluster(filtered)
    os.makedirs(args.out, exist_ok=True)

    with open(os.path.join(args.out, "candidates.json"), "w", encoding="utf-8", newline="\n") as handle:
        json.dump({"known": known, "candidates": rows}, handle, ensure_ascii=False, indent=2)

    lines = [f"# 待审 tag（{len(filtered)} 个不同 / {len(rows)} 个簇）", "",
             f"已在白名单里的：{known} 个（已跳过，不用审）", "",
             "| 次数 | 变体 |", "|---|---|"]
    for row in rows[:200]:
        variants = " / ".join(f"{name} ({count})" for name, count in row["variants"][:4])
        lines.append(f"| {row['total']} | {variants} |")
    with open(os.path.join(args.out, "review.md"), "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(lines) + "\n")

    candidate_lines = "\n".join(
        f"- {row['total']} | " + " / ".join(name for name, _ in row["variants"][:3])
        for row in rows[:200])
    with open(os.path.join(args.out, "prompt.md"), "w", encoding="utf-8", newline="\n") as handle:
        handle.write(PROMPT % candidate_lines)

    print(f"候选 {len(filtered)} 个，聚成 {len(rows)} 个簇 → {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
