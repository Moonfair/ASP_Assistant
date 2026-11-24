"""Rebuild the small ID -> in-game name ledger from pinned CN game data.

Build-time/network optional: python scripts/quick_chat/refresh_enemies.py
Only the generated quick-chat.json is consumed by the apps at runtime.
"""
import argparse
import csv
import hashlib
import io
import json
import re
from collections import defaultdict
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from urllib.request import urlopen

ROOT = Path(__file__).resolve().parents[2]
LEDGER = Path(__file__).with_name("enemies.tsv")
REPO = "Kengxxiao/ArknightsGameData"
REV = "7e560050b03c34661db1d322d008733072352b2a"
SEASON = "act3autochess"
TREE_SHA = "0f76e247b7644681fbec88e116fe4f5ff72b40c5"
BASE = f"https://raw.githubusercontent.com/{REPO}/{REV}/zh_CN/gamedata"
ID = re.compile(r"\benemy_\d{4,}_[A-Za-z][A-Za-z0-9_]*\b")


def ids(value):
    # IDs can be object keys, values, or a comma-separated blackboard parameter.
    return set(ID.findall(json.dumps(value, ensure_ascii=False)))


def fetch(path, url, *, blob=None):
    raw = path.read_bytes() if path.exists() else None
    if raw is None:
        with urlopen(url, timeout=60) as response:
            raw = response.read()
    if blob and hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\0" + raw).hexdigest() != blob:
        raise ValueError(f"Level blob mismatch: {path}")
    result = json.loads(raw)
    if not path.exists():
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(raw)
    return result


def build(cache):
    def table(name, directory):
        return fetch(cache / (name + ".json"), f"{BASE}/{directory}/{name}.json")

    activity = table("activity_table", "excel")
    season = activity["activity"]["AUTOCHESS"][SEASON]["V3_1"]
    handbook = table("enemy_handbook_table", "excel")["enemyData"]
    db = {r["Key"]: r["Value"] for r in table("enemy_database", "levels/enemydata")["enemies"]}
    sources = defaultdict(set)
    for key, config in season.items():
        for enemy in ids(config):
            sources[enemy].add(key)
    for boss in season["bossInfoDict"]:
        enemy = activity["autoChessData"]["bossInfoDict"][boss]["enemyId"]
        sources[enemy].add("boss:" + boss)

    # Includes every map subregion, normal/multi/hidden boss wave and enemy override file.
    tree = fetch(cache / "tree0.json", f"https://api.github.com/repos/{REPO}/git/trees/{TREE_SHA}?recursive=1")
    if tree["sha"] != TREE_SHA or tree["truncated"]:
        raise ValueError("Incomplete season level tree")
    levels = {SEASON + "/" + r["path"]: r["sha"] for r in tree["tree"]
              if r["type"] == "blob" and r["path"].endswith(".json")}
    # Season data references a few legacy training templates outside the act3 folder.
    for section in (season, season["trainingData"]):
        for mode in section["battleDataDict"].values():
            for turn in mode.values():
                for wave in turn:
                    path = wave["levelId"].lower().removeprefix("activities/") + ".json"
                    levels.setdefault(path, None)

    def level(item):
        path, sha = item
        value = fetch(cache / path, f"{BASE}/levels/activities/{path}", blob=sha)
        return ids(value)

    with ThreadPoolExecutor(max_workers=6) as pool:
        for found in pool.map(level, levels.items()):
            for enemy in found:
                sources[enemy].add("level")

    # Include units explicitly referenced by a unit's skills/talents, recursively.
    # Do not follow prefab IDs or handbook's (often unrelated) cross-links.
    pending = list(sources)
    while pending:
        enemy = pending.pop()
        for row in db[enemy]:
            data = row["enemyData"]
            for child in ids([data["skills"], data["talentBlackboard"]]):
                if child not in sources:
                    pending.append(child)
                sources[child].add("skill")
    out = io.StringIO(newline="")
    writer = csv.writer(out, delimiter="\t", lineterminator="\n")
    writer.writerow(["id", "name", "sources"])
    for enemy, origin in sorted(sources.items()):
        name = handbook[enemy]["name"].strip()  # Some upstream names carry a trailing newline.
        if not name or "\n" in name or "\t" in name:
            raise ValueError(f"No display name: {enemy}")
        writer.writerow([enemy, name, ",".join(sorted(origin))])
    return out.getvalue(), len(levels)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache", type=Path, default=ROOT / "artifacts" / f"quick-chat-{REV}")
    parser.add_argument("--check", action="store_true", help="Compare against the checked-in ledger")
    args = parser.parse_args()
    result, n_levels = build(args.cache)
    if args.check:
        if result != LEDGER.read_text(encoding="utf-8"):
            raise SystemExit("Enemy ledger differs; review and refresh")
    else:
        LEDGER.write_text(result, encoding="utf-8", newline="\n")
    print(f"{len(result.splitlines()) - 1} IDs from season, {n_levels} level files and skill data")
