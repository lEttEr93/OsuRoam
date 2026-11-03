"""审核并生成 tag 池（Resources/tag-pool.json）。

用法：
    python Tools/build-tag-pool.py <tag-counts.json 路径> [--out 输出路径]

输入是审计者的谱面库统计（形如 {"electronic": 2883, ...}），用来给池子排序、
并检查哪些条目在真实数据里根本不存在。池子本身是人工维护的白名单：
**只有这里列出的 tag 才会进入"可选 tag"列表**，爬到的其它 tag 进 SeenTags。

原则：
- 别名只收**同一样东西的不同写法**（dnb / drum and bass、j-pop / jpop、jacks / jack）；
  子流派（heavy metal / death metal）各自独立，不做层层归并，否则用户想精确筛就筛不了。
- 组内顺序按真实出现次数降序，稳定排序。
- 要加新 tag：改下面的 TABLE，重跑脚本，检查报告里 count=0 的条目是否真的需要。
"""
import json
import os
import sys

# 分组顺序决定界面上 tag 的排列顺序（靠前的组优先显示）。
TABLE = [
    ("genre", "曲风", [
        ("electronic", []),
        ("edm", []),
        ("instrumental", []),
        ("pop", []),
        ("jpop", ["j-pop"]),
        ("kpop", ["k-pop"]),
        ("cantopop", ["c-pop"]),
        ("city pop", []),
        ("rock", []),
        ("alternative rock", []),
        ("indie rock", []),
        ("pop rock", []),
        ("post rock", []),
        ("hard rock", []),
        ("progressive rock", []),
        ("art rock", []),
        ("gothic rock", []),
        ("metal", []),
        ("heavy metal", []),
        ("death metal", []),
        ("black metal", []),
        ("power metal", []),
        ("nu metal", []),
        ("metalcore", []),
        ("post metal", []),
        ("punk", []),
        ("hardcore", []),
        ("happy hardcore", ["happycore"]),
        ("speedcore", []),
        ("breakcore", []),
        ("artcore", []),
        ("drum and bass", ["dnb", "drum n bass", "d&b"]),
        ("liquid drum and bass", ["liquid funk", "liquid dnb"]),
        ("neurofunk", []),
        ("crossbreed", []),
        ("drumstep", []),
        ("dubstep", []),
        ("hardstyle", []),
        ("gabber", ["uptempo"]),
        ("trance", []),
        ("progressive trance", []),
        ("psytrance", []),
        ("house", []),
        ("deep house", []),
        ("tech house", []),
        ("progressive house", []),
        ("techno", []),
        ("j-core", ["jcore", "j core"]),
        ("chiptune", ["8bit", "8-bit"]),
        ("future bass", []),
        ("color bass", ["colour bass"]),
        ("phonk", []),
        ("midtempo", []),
        ("synthwave", ["retrowave"]),
        ("ambient", []),
        ("orchestral", ["symphonic"]),
        ("classical", []),
        ("piano", []),
        ("guitar", []),
        ("jazz", []),
        ("jazz fusion", []),
        ("blues", []),
        ("funk", []),
        ("soul", []),
        ("hip hop", ["hip-hop", "hiphop"]),
        ("rnb", ["r&b", "rhythm and blues"]),
        ("reggae", []),
        ("latin", []),
        ("folk", []),
        ("country", []),
        ("anime", []),
        ("video game music", ["video game", "game music", "vgm"]),
        ("touhou", ["toho"]),
        ("vocaloid", []),
        ("denpa", []),
        ("idol", []),
        ("eurobeat", []),
        ("nightcore", []),
        ("jrock", ["j-rock"]),
        ("original soundtrack", ["ost", "soundtrack"]),
    ]),
    ("meta", "曲目属性", [
        ("original", []),
        ("remix", []),
        ("cover", []),
        ("arrange", []),
        ("mashup", []),
        ("medley", ["mega mix"]),
        ("dump", []),
        ("meme", ["joke"]),
        ("vocal", []),
        ("female vocal", []),
        ("male vocal", []),
        ("duet", []),
    ]),
    ("language", "语言", [
        ("japanese", []),
        ("english", []),
        ("chinese", []),
        ("korean", []),
        ("french", []),
        ("german", []),
        ("spanish", []),
        ("italian", []),
        ("russian", []),
        ("polish", []),
        ("swedish", []),
        ("indonesian", []),
        ("portuguese", []),
        ("multilingual", []),
        ("no lyrics", []),
    ]),
    ("source", "来源/活动", [
        ("featured artist", ["featured"]),
        ("mappers' guild", ["mappers guild"]),
        ("osu!", ["osu"]),
        ("bms", []),
        ("bemani", []),
        ("sdvx", ["sound voltex"]),
        ("rhythm game", []),
        ("contest", []),
        ("tournament", []),
        ("osu!mania world cup", ["mwc", "world cup"]),
        ("cup", []),
        ("dash", []),
        ("fighters", []),
        ("finals", []),
    ]),
    ("keymode", "键数", [
        ("4k", ["4key"]),
        ("5k", ["5key"]),
        ("6k", ["6key"]),
        ("7k", ["7key"]),
        ("8k", ["8key"]),
        ("9k", ["9key"]),
        ("10k", ["10key"]),
    ]),
    ("pattern", "键型（1.5.2 将废弃）", [
        ("stream", ["streams"]),
        ("jumpstream", []),
        ("handstream", []),
        ("jack", ["jacks"]),
        ("chordjack", ["chordjacks"]),
        ("minijack", ["minijacks"]),
        ("longjack", ["longjacks"]),
        ("trill", ["trills"]),
        ("ln", ["long note", "long notes", "noodle", "noodles"]),
        ("rice", []),
        ("tech", ["technical", "tech map"]),
        ("speed", []),
        ("stamina", []),
        ("sv", ["scroll"]),
        ("vibro", []),
        ("density", ["dense"]),
        ("chord", ["chords"]),
        ("jump", ["jumps"]),
        ("hand", ["hands"]),
        ("spread", []),
        ("coordination", []),
        ("release", []),
        ("shield", []),
        ("inverse", []),
    ]),
]


def main() -> int:
    if len(sys.argv) < 2:
        sys.stderr.write(__doc__)
        return 2
    counts = json.load(open(sys.argv[1], encoding="utf-8"))
    out_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Resources", "tag-pool.json")
    for index, argument in enumerate(sys.argv):
        if argument == "--out" and index + 1 < len(sys.argv):
            out_path = sys.argv[index + 1]

    groups = []
    aliases = {}
    missing = []
    seen_names = set()

    for group_id, label, entries in TABLE:
        rows = []
        for canonical, synonyms in entries:
            total = counts.get(canonical, 0)
            for synonym in synonyms:
                total += counts.get(synonym, 0)
                if synonym in aliases and aliases[synonym] != canonical:
                    raise SystemExit(f"别名冲突：{synonym} -> {aliases[synonym]} / {canonical}")
                aliases[synonym] = canonical
            if canonical in seen_names:
                raise SystemExit(f"重复条目：{canonical}")
            seen_names.add(canonical)
            if total == 0:
                missing.append(f"{group_id}/{canonical}")
            rows.append({"name": canonical, "count": total})
        # 组内按真实出现次数降序，同频按字母序，保证结果稳定
        rows.sort(key=lambda row: (-row["count"], row["name"]))
        groups.append({
            "id": group_id,
            "label": label,
            "tags": [row["name"] for row in rows],
        })

    payload = {
        "version": 2,
        "audited": "2026-11-03",
        "note": "白名单池：只有这里列出的 tag 会进入可选列表；组内顺序按审计时的真实出现次数排。",
        "groups": groups,
        "aliases": dict(sorted(aliases.items())),
    }
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    with open(out_path, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(payload, handle, ensure_ascii=False, indent=2)
        handle.write("\n")

    total = sum(len(group["tags"]) for group in groups)
    print(f"写入 {out_path}")
    print(f"分组 {len(groups)}，条目 {total}，别名 {len(aliases)}")
    if missing:
        print(f"警告：{len(missing)} 个条目在当前库里没出现（可能是真冷门，也可能拼错）")
        for item in missing:
            print("  -", item)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
