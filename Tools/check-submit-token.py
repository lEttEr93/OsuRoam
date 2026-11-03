"""检查 data/tag-submit-token.txt 是不是一个「只能开 issue」的细粒度 PAT。

    python Tools/check-submit-token.py

两条断言：
1. 能读 issues 列表（说明 Issues 权限生效）；
2. 读不了仓库内容（说明没有 Contents 权限 —— 这是我们想要的）。

任何一条不符合都会打印原因并以非零码退出。脚本只发 GET，不会修改仓库。
"""
import json
import os
import sys
import urllib.error
import urllib.request

REPO = "lEttEr93/OsuRoam"
TOKEN_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "data", "tag-submit-token.txt")


def call(path, token):
    request = urllib.request.Request(f"https://api.github.com{path}")
    request.add_header("Authorization", f"Bearer {token}")
    request.add_header("Accept", "application/vnd.github+json")
    request.add_header("User-Agent", "osu-roam-token-check")
    try:
        with urllib.request.urlopen(request, timeout=30) as handle:
            return handle.status, handle.read().decode()[:200]
    except urllib.error.HTTPError as error:
        return error.code, error.read().decode()[:200]


def main():
    if not os.path.exists(TOKEN_PATH):
        print(f"找不到 {TOKEN_PATH}")
        print("把 GitHub 生成的 github_pat_... 整串粘进这个文件（只放 token，不要引号、不要换行说明）。")
        return 2

    token = open(TOKEN_PATH, encoding="utf-8-sig").read().strip()
    if not token:
        print("token 文件是空的")
        return 2
    print(f"token 前缀：{token[:11]}…  长度：{len(token)}")
    if not token.startswith("github_pat_"):
        print("警告：细粒度 PAT 应该以 github_pat_ 开头，这个可能是 classic token（权限通常过大）")

    status, body = call(f"/repos/{REPO}/issues?per_page=1", token)
    if status != 200:
        print(f"[失败] 读 issues 返回 {status}：{body}")
        print("检查：仓库是否选对、Issues 权限是否给了 Read，token 是否过期")
        return 1
    print("[通过] Issues 可读")

    status, body = call(f"/repos/{REPO}/contents/data", token)
    if status in (403, 404):
        print(f"[通过] 读不了仓库内容（{status}），说明没有给 Contents")
        return 0
    if status == 200:
        print("[警告] 这个 token 能读仓库内容 → 权限给多了，建议把 Contents 改成 No access")
        return 1
    print(f"[未知] 读仓库内容返回 {status}：{body}")
    return 1


if __name__ == "__main__":
    sys.exit(main())
