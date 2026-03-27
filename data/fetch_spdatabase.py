#!/usr/bin/env python3
"""
SPDatabase 数据抓取工具
从 PRTS Wiki 获取卫戍协议/盟约数据并整理成 JSON 文件

数据来源: https://static.prts.wiki/app/spdatabase/index.html
参考项目: https://github.com/Yanstory/SPDatabase
"""

import json
import sys
import urllib.request
from pathlib import Path

# ── 常量 ────────────────────────────────────────────────────────────────────

TORAPPU_ENDPOINT = "https://torappu.prts.wiki"
ARCHIVE_ENDPOINT = "https://static.prts.wiki/app/spdatabase"

PROFESSION_MAP = {
    "PIONEER": "先锋",
    "WARRIOR": "近卫",
    "SNIPER": "狙击",
    "SUPPORT": "辅助",
    "CASTER": "术师",
    "SPECIAL": "特种",
    "MEDIC": "医疗",
    "TANK": "重装",
}

SP_CHAR_REDIRECT = {
    "char_601_cguard": "预备干员-近卫(卫戍协议)",
    "char_607_cspec": "预备干员-特种(卫戍协议)",
    "char_600_cpione": "预备干员-先锋(卫戍协议)",
    "char_605_cmedic": "预备干员-医疗(卫戍协议)",
    "char_604_ccast": "预备干员-术师(卫戍协议)",
    "char_603_csnipe": "预备干员-狙击(卫戍协议)",
    "char_602_cdfend": "预备干员-重装(卫戍协议)",
    "char_606_csuppo": "预备干员-辅助(卫戍协议)",
    "char_612_accast": "Pith(卫戍协议)",
    "char_614_acsupo": "Raidian(卫戍协议)",
    "char_609_acguad": "Sharp(卫戍协议)",
    "char_610_acfend": "Mechanist(卫戍协议)",
    "char_611_acnipe": "Stormeye(卫戍协议)",
    "char_615_acspec": "Misery(卫戍协议)",
    "char_608_acpion": "郁金香(卫戍协议)",
    "char_613_acmedc": "Touch(卫戍协议)",
}

BOND_BAN = {"绝技"}

# 赛季配置
SP_SEASONS = {
    1: {
        "mode": "ACTIVITY_SEASON",
        "seasonCode": "act2autochess",
        "name": "盟约 下半 (Alliance 2nd Phase)",
    },
    2: {
        "mode": "ACTIVITY_SEASON_ARC",
        "seasonCode": "act1autochess",
        "archiveFile": "alliance_1st.json",
        "name": "盟约 (Alliance)",
    },
}

# ── 网络请求 ─────────────────────────────────────────────────────────────────

def fetch_json(url: str) -> dict:
    print(f"  正在获取: {url}")
    req = urllib.request.Request(url, headers={"User-Agent": "SPDatabase-Scraper/1.0"})
    with urllib.request.urlopen(req, timeout=30) as resp:
        return json.loads(resp.read().decode("utf-8"))

# ── 数据处理 ─────────────────────────────────────────────────────────────────

def process_season(season_no: int, op_data: dict, act_data: dict) -> dict:
    """处理单个赛季数据，返回整理后的结构"""
    season_cfg = SP_SEASONS[season_no]
    season_key = season_cfg["seasonCode"]

    bond_meta_list = act_data["autoChessData"]["bondInfoDict"]
    season_data = act_data["activity"]["AUTOCHESS_SEASON"][season_key]
    garrison_list = season_data["garrisonDataDict"]
    char_chess_dict = season_data["charChessDataDict"]
    char_shop_dict = season_data["charShopChessDatas"]

    # 盟约分类
    core_bonds = [
        {"key": b["bondId"], "name": b["name"]}
        for b in bond_meta_list.values()
        if b["bondType"] == "SEASON"
    ]
    extra_bonds = [
        {"key": b["bondId"], "name": b["name"]}
        for b in bond_meta_list.values()
        if b["bondType"] == "REGULAR" and b["name"] not in BOND_BAN
    ]

    # 特质词条去重
    trait_set = {}
    for gar in garrison_list.values():
        desc = gar["eventTypeDesc"]
        if desc not in trait_set:
            trait_set[desc] = desc

    op_list = {}

    for chess_id, chess in char_chess_dict.items():
        # 只处理有升级路径的非 diy 棋子（即基础版本）
        if "diy" in chess_id or not chess.get("upgradeChessId"):
            continue

        shop_chess = char_shop_dict.get(chess_id)
        if not shop_chess:
            continue

        char_id = shop_chess["charId"]

        # 初始化干员条目
        if char_id not in op_list:
            char_raw = op_data.get(char_id, {})
            rarity_raw = char_raw.get("rarity", "TIER_0")
            op_list[char_id] = {
                "id": char_id,
                "rarity": rarity_raw.replace("TIER_", ""),
                "name": char_raw.get("name", char_id),
                "profession": PROFESSION_MAP.get(char_raw.get("profession", ""), char_raw.get("profession", "")),
                "link": SP_CHAR_REDIRECT.get(char_id),
                "chess": {},
            }

        rank = shop_chess["chessLevel"]

        def build_chess_entry(c):
            return {
                "rank": rank,
                "bonds": [
                    {"id": b, "name": bond_meta_list[b]["name"]}
                    for b in c.get("bondIds", [])
                ],
                "gars": [
                    garrison_list[g]
                    for g in c.get("garrisonIds", [])
                    if g in garrison_list
                ],
            }

        op_list[char_id]["chess"]["normal"] = build_chess_entry(chess)

        # 精锐版本
        upgrade_id = chess["upgradeChessId"]
        upgrade_chess = char_chess_dict.get(upgrade_id)
        if upgrade_chess:
            op_list[char_id]["chess"]["elite"] = build_chess_entry(upgrade_chess)

    # 将 chess dict 转成列表，便于阅读
    for op in op_list.values():
        op["chess"] = {
            "normal": op["chess"].get("normal"),
            "elite": op["chess"].get("elite"),
        }

    return {
        "seasonNo": season_no,
        "seasonName": season_cfg["name"],
        "seasonCode": season_key,
        "meta": {
            "coreBonds": core_bonds,
            "extraBonds": extra_bonds,
            "traits": list(trait_set.keys()),
        },
        "operators": list(op_list.values()),
    }


def main():
    output_path = Path("spdatabase_output.json")

    print("=== SPDatabase 数据抓取工具 ===\n")

    # 1. 获取公共数据
    print("[1/4] 获取干员数据 (character_table.json)...")
    op_data = fetch_json(f"{TORAPPU_ENDPOINT}/gamedata/latest/excel/character_table.json")
    print(f"      共 {len(op_data)} 名干员\n")

    print("[2/4] 获取活动数据 (activity_table.json)...")
    act_data = fetch_json(f"{TORAPPU_ENDPOINT}/gamedata/latest/excel/activity_table.json")
    print()

    print("[3/4] 获取存档数据 (alliance_1st.json)...")
    arc_data = fetch_json(f"{ARCHIVE_ENDPOINT}/archive/alliance_1st.json")
    print()

    # 2. 处理各赛季
    print("[4/4] 处理数据...\n")

    seasons_data = []

    print("  处理赛季 1: 盟约 下半...")
    season1 = process_season(1, op_data, act_data)
    seasons_data.append(season1)
    print(f"      干员数: {len(season1['operators'])}")

    print("  处理赛季 2: 盟约...")
    season2 = process_season(2, op_data, arc_data)
    seasons_data.append(season2)
    print(f"      干员数: {len(season2['operators'])}")

    # 3. 输出 JSON
    result = {
        "source": "https://static.prts.wiki/app/spdatabase/index.html",
        "seasons": seasons_data,
    }

    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=2)

    print(f"\n完成！数据已写入: {output_path.resolve()}")
    print(f"文件大小: {output_path.stat().st_size / 1024:.1f} KB")


if __name__ == "__main__":
    main()
