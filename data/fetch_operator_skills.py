#!/usr/bin/env python3
"""Build the local skill supplement for the operators in the active catalog."""

import argparse
import json
import re
from contextlib import closing
from http.client import HTTPSConnection
from urllib.parse import quote, urlsplit
from pathlib import Path

from fetch_spdatabase import SP_CHAR_REDIRECT, TORAPPU_ENDPOINT, fetch_json, strip_rich_text

DATA = Path(__file__).parent
EXCEL = f"{TORAPPU_ENDPOINT}/gamedata/latest/excel"
SKILL_ICONS_PATH = "/assets/skill_icon"
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"

# Touch in this catalog is the recruitable Integrated Strategies operator.
CHARACTER_OVERRIDES = {name: char_id for char_id, name in SP_CHAR_REDIRECT.items()}
CHARACTER_OVERRIDES["Touch"] = "char_510_amedic"
SP_TYPES = {"INCREASE_WITH_TIME": "自动回复", "INCREASE_WHEN_ATTACK": "攻击回复",
            "INCREASE_WHEN_TAKEN_DAMAGE": "受击回复"}
ACTIVATIONS = {"MANUAL": "手动触发", "AUTO": "自动触发", "PASSIVE": "被动"}


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8"))


def describe(level):
    values = {entry["key"].lower(): entry["value"] for entry in level["blackboard"]}

    def substitute(match):
        negative, key, spec = match.groups()
        value = values[key.lower()] * (-1 if negative else 1)
        if spec and spec.endswith("%"):
            return f"{value:.{spec.count('0') - 1}%}"
        if spec:
            decimals = len(spec.partition(".")[2])
            return f"{value:.{decimals}f}"
        return f"{value:g}"

    description = strip_rich_text(level["description"] or "").replace(r"\n", "\n")
    rendered = re.sub(r"\{(-?)([^{}:]+)(?::(0(?:\.0+)?%?))?\}", substitute, description)
    if re.search(r"\{[^{}]+\}", rendered):
        raise ValueError(f"{level['name']} 的技能说明仍有占位符：{rendered}")
    return rendered


def build_skills(operators, characters, skills):
    candidates = {}
    for char_id, character in characters.items():
        if char_id.startswith("char_") and any(slot.get("skillId") for slot in character.get("skills", [])):
            candidates.setdefault(character["name"], []).append(char_id)

    result = {}
    for operator in operators:
        name = operator["name"]
        matches = candidates.get(name, [])
        char_id = CHARACTER_OVERRIDES.get(name)
        if not char_id:
            if len(matches) > 1:
                raise ValueError(f"多个本体干员与 {name} 同名，需明确对应关系")
            if not matches:
                if operator.get("strategyOnly"):
                    continue
                raise ValueError(f"找不到 {name} 的本体技能")
            char_id = matches[0]

        entries = []
        for index, slot in enumerate(characters[char_id]["skills"], 1):
            if not slot.get("skillId"):
                continue
            skill = skills[slot["skillId"]]
            icon_id = skill["iconId"] or skill["skillId"]
            if not re.fullmatch(r"[a-zA-Z0-9_\[\]-]+", icon_id):
                raise ValueError(f"无效的技能图标 ID：{icon_id}")
            levels = skill["levels"]
            number = min(len(levels), 7 + len(slot.get("levelUpCostCond") or []))
            level = levels[number - 1]
            sp = level["spData"]
            entries.append({
                "slot": index,
                "name": level["name"],
                "iconPath": f"icons/skills/{icon_id}.png",
                "level": f"专{['一', '二', '三'][number - 8]}" if number > 7 else f"{number} 级",
                "description": describe(level),
                "recovery": SP_TYPES.get(sp["spType"], ""),
                "activation": ACTIVATIONS[level["skillType"]],
                "recoveryPerSecond": sp["increment"] if sp["spType"] == "INCREASE_WITH_TIME" else 0,
                "spCost": sp["spCost"],
                "initialSp": sp["initSp"],
                "duration": max(0, level["duration"]),
                "charges": sp["maxChargeTime"],
            })
        if entries:
            result[name] = entries
    return result


def fetch_skill_icon(connection, icon_path):
    path = DATA / icon_path
    if path.is_file() and path.read_bytes().startswith(PNG_SIGNATURE):
        return
    filename = quote("skill_icon_" + path.name)
    connection.request("GET", f"{SKILL_ICONS_PATH}/{filename}", headers={"User-Agent": "SPDatabase-Scraper/1.0"})
    response = connection.getresponse()
    content = response.read()
    if response.status != 200 or not content.startswith(PNG_SIGNATURE):
        raise ValueError(f"无法获取技能图标 {path.name}（HTTP {response.status}）")
    path.write_bytes(content)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--characters", type=Path)
    parser.add_argument("--skills", type=Path)
    args = parser.parse_args()
    characters = read_json(args.characters) if args.characters else fetch_json(f"{EXCEL}/character_table.json")
    skills = read_json(args.skills) if args.skills else fetch_json(f"{EXCEL}/skill_table.json")
    season = read_json(DATA / "source_metadata.json")["seasonCode"]
    entries = build_skills(read_json(DATA / "operators.json")["operators"], characters, skills)
    icons = sorted({skill["iconPath"] for skills in entries.values() for skill in skills})
    (DATA / "icons" / "skills").mkdir(parents=True, exist_ok=True)
    with closing(HTTPSConnection(urlsplit(TORAPPU_ENDPOINT).hostname, timeout=20)) as connection:
        for icon_path in icons:
            fetch_skill_icon(connection, icon_path)
    lines = [f'  "season": {json.dumps(season)},', '  "operators": {']
    for index, (name, values) in enumerate(entries.items()):
        suffix = "," if index < len(entries) - 1 else ""
        lines.append(f"    {json.dumps(name, ensure_ascii=False)}: {json.dumps(values, ensure_ascii=False)}{suffix}")
    (DATA / "operator_skills.json").write_text(
        "\n".join(["{", *lines, "  }", "}", ""]), encoding="utf-8", newline="\n")
    print(f"已生成 {len(entries)} 名干员的本体技能和 {len(icons)} 张技能图标")


if __name__ == "__main__":
    main()
