#!/usr/bin/env python3
"""
SPDatabase 数据抓取工具
从 PRTS Wiki 获取卫戍协议/盟约数据，输出 operators.json 和 equipment.json

数据来源: https://static.prts.wiki/app/spdatabase/index.html
参考项目: https://github.com/Yanstory/SPDatabase
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Optional

# ── 常量 ────────────────────────────────────────────────────────────────────

TORAPPU_ENDPOINT = "https://torappu.prts.wiki"
ARCHIVE_ENDPOINT = "https://static.prts.wiki/app/spdatabase"
MEDIA_ENDPOINT = "https://media.prts.wiki"

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
        "seasonCode": "act2autochess",
        "name": "盟约 下半 (Alliance 2nd Phase)",
        "mode": "ACTIVITY_SEASON",
    },
    2: {
        "seasonCode": "act1autochess",
        "name": "盟约 (Alliance)",
        "mode": "ACTIVITY_SEASON_ARC",
        "archiveFile": "alliance_1st.json",
    },
}

# ── 工具函数 ──────────────────────────────────────────────────────────────────

def fetch_json(url: str) -> dict:
    print(f"  正在获取: {url}")
    req = urllib.request.Request(url, headers={"User-Agent": "SPDatabase-Scraper/1.0"})
    with urllib.request.urlopen(req, timeout=30) as resp:
        return json.loads(resp.read().decode("utf-8"))


def strip_rich_text(text: str) -> str:
    return re.sub(r'<@[^>]*>|</>', '', text)


def get_prts_media_url(filename: str) -> str:
    """构造 PRTS Media Wiki 图片 URL（MD5 路径）"""
    md5 = hashlib.md5(filename.encode("utf-8")).hexdigest()
    encoded = urllib.parse.quote(filename)
    return f"{MEDIA_ENDPOINT}/{md5[0]}/{md5[:2]}/{encoded}"


def download_image(url: str, save_path: Path) -> bool:
    """下载图片到本地，已存在则跳过。返回是否新下载。"""
    if save_path.exists():
        return False
    try:
        req = urllib.request.Request(url, headers={"User-Agent": "SPDatabase-Scraper/1.0"})
        with urllib.request.urlopen(req, timeout=15) as resp:
            save_path.write_bytes(resp.read())
        return True
    except Exception as e:
        print(f"    [FAIL] {save_path.name}: {e}")
        return False


# ── 干员处理 ──────────────────────────────────────────────────────────────────

def build_traits(chess_entry: dict, garrison_dict: dict) -> list[dict]:
    """从棋子条目构建特质列表"""
    traits = []
    for gar_id in chess_entry.get("garrisonIds", []):
        gar = garrison_dict.get(gar_id)
        if gar:
            traits.append({
                "traitType": gar["eventTypeDesc"],
                "traitDescription": strip_rich_text(gar["garrisonDesc"]),
            })
    return traits


def classify_bonds(chess_entry: dict, bond_info_dict: dict) -> tuple[str | None, list[str]]:
    """分类盟约为核心盟约和附加盟约"""
    core_covenant = None
    additional_covenants = []

    for bond_id in chess_entry.get("bondIds", []):
        bond = bond_info_dict.get(bond_id)
        if not bond:
            continue
        bond_name = bond["name"]
        bond_type = bond["bondType"]

        if bond_type == "SEASON" and core_covenant is None:
            core_covenant = bond_name
        elif bond_type == "REGULAR" and bond_name not in BOND_BAN:
            additional_covenants.append(bond_name)

    return core_covenant, additional_covenants


def process_operators(
    char_table: dict,
    season_data: dict,
    bond_info_dict: dict,
) -> list[dict]:
    """处理干员数据，返回匹配 C# 模型的列表"""
    char_chess_dict = season_data["charChessDataDict"]
    char_shop_dict = season_data["charShopChessDatas"]
    garrison_dict = season_data["garrisonDataDict"]

    operators = []

    for chess_id, chess in char_chess_dict.items():
        # 只处理有升级路径的非 diy 棋子（即基础版本）
        if "diy" in chess_id or not chess.get("upgradeChessId"):
            continue

        shop_entry = char_shop_dict.get(chess_id)
        if not shop_entry:
            continue

        char_id = shop_entry["charId"]
        tier = shop_entry["chessLevel"]

        # 获取干员名称
        if char_id in SP_CHAR_REDIRECT:
            name = SP_CHAR_REDIRECT[char_id]
        else:
            char_raw = char_table.get(char_id, {})
            name = char_raw.get("name", char_id)

        # 盟约分类
        core_covenant, additional_covenants = classify_bonds(chess, bond_info_dict)

        # 普通版本特质
        normal_traits = build_traits(chess, garrison_dict)

        # 精锐版本特质
        upgrade_id = chess["upgradeChessId"]
        upgrade_chess = char_chess_dict.get(upgrade_id)
        elite_traits = build_traits(upgrade_chess, garrison_dict) if upgrade_chess else []

        operator = {
            "name": name,
            "iconPath": f"icons/operators/{name}.png",
            "_charId": char_id,
            "tier": tier,
            "coreCovenant": core_covenant or "",
            "additionalCovenants": additional_covenants,
            "normal": {
                "traits": normal_traits,
            },
            "elite": {
                "traits": elite_traits,
            },
        }
        operators.append(operator)

    return operators


# ── 装备处理 ──────────────────────────────────────────────────────────────────

def process_equipment(season_data: dict) -> list[dict]:
    """处理装备数据，返回匹配 C# 模型的列表"""
    trap_chess_dict = season_data["trapChessDataDict"]
    trap_shop_dict = season_data["trapShopChessDatas"]
    effect_info_dict = season_data["effectInfoDataDict"]

    equipment_list = []

    for item_id, shop_entry in trap_shop_dict.items():
        if shop_entry.get("itemType") != "EQUIP":
            continue

        tier = shop_entry["itemLevel"]
        golden_item_id = shop_entry.get("goldenItemId")

        # 普通版本
        normal_chess = trap_chess_dict.get(item_id)
        if not normal_chess:
            continue

        normal_effect_id = normal_chess.get("effectId")
        normal_effect = effect_info_dict.get(normal_effect_id, {}) if normal_effect_id else {}
        name = normal_effect.get("effectName", item_id)
        normal_desc = strip_rich_text(normal_effect.get("effectDesc", ""))

        # 精锐（金色）版本
        elite_desc = ""
        if golden_item_id:
            golden_chess = trap_chess_dict.get(golden_item_id)
            if golden_chess:
                golden_effect_id = golden_chess.get("effectId")
                golden_effect = effect_info_dict.get(golden_effect_id, {}) if golden_effect_id else {}
                elite_desc = strip_rich_text(golden_effect.get("effectDesc", ""))

        equip = {
            "name": name,
            "iconPath": f"icons/equipment/{name}.png",
            "tier": tier,
            "normal": {
                "effectDescription": normal_desc,
            },
            "elite": {
                "effectDescription": elite_desc,
            },
        }
        equipment_list.append(equip)

    return equipment_list


# ── 主流程 ────────────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description="SPDatabase 数据抓取工具")
    parser.add_argument(
        "--season",
        type=int,
        default=1,
        choices=SP_SEASONS.keys(),
        help="赛季编号 (默认: 1 = 最新赛季)",
    )
    parser.add_argument("--skip-icons", action="store_true", help="跳过图标下载")
    args = parser.parse_args()

    season_no = args.season
    season_cfg = SP_SEASONS[season_no]
    season_code = season_cfg["seasonCode"]
    data_dir = Path(__file__).parent

    print(f"=== SPDatabase 数据抓取工具 ===")
    print(f"赛季: {season_cfg['name']} (season {season_no})\n")

    # 1. 获取干员名称表
    print("[1/2] 获取干员数据 (character_table.json)...")
    char_table = fetch_json(
        f"{TORAPPU_ENDPOINT}/gamedata/latest/excel/character_table.json"
    )
    print(f"      共 {len(char_table)} 名干员\n")

    # 2. 获取活动/存档数据
    if season_cfg["mode"] == "ACTIVITY_SEASON_ARC":
        archive_file = season_cfg["archiveFile"]
        print(f"[2/2] 获取存档数据 ({archive_file})...")
        act_data = fetch_json(f"{ARCHIVE_ENDPOINT}/archive/{archive_file}")
    else:
        print("[2/2] 获取活动数据 (activity_table.json)...")
        act_data = fetch_json(
            f"{TORAPPU_ENDPOINT}/gamedata/latest/excel/activity_table.json"
        )

    print()

    # 3. 定位赛季数据和全局盟约信息
    bond_info_dict = act_data["autoChessData"]["bondInfoDict"]
    season_data = act_data["activity"]["AUTOCHESS_SEASON"][season_code]

    # 4. 处理干员
    print("处理干员数据...")
    operators = process_operators(char_table, season_data, bond_info_dict)
    # Deduplicate by name (keep first occurrence)
    seen_names: set[str] = set()
    unique_operators = []
    for op in operators:
        if op["name"] not in seen_names:
            seen_names.add(op["name"])
            unique_operators.append(op)
    if len(unique_operators) < len(operators):
        print(f"  重复干员 (已去重): {len(operators) - len(unique_operators)}")
    operators = unique_operators
    print(f"  干员数: {len(operators)}")

    # 5. 处理装备
    print("处理装备数据...")
    equipment = process_equipment(season_data)
    print(f"  装备数: {len(equipment)}")

    # 6. 下载图标
    if not args.skip_icons:
        op_icon_dir = data_dir / "icons" / "operators"
        eq_icon_dir = data_dir / "icons" / "equipment"
        op_icon_dir.mkdir(parents=True, exist_ok=True)
        eq_icon_dir.mkdir(parents=True, exist_ok=True)

        print("下载干员图标...")
        dl_new, dl_skip, dl_fail = 0, 0, 0
        for op in operators:
            char_id = op.get("_charId", "")
            if not char_id:
                dl_fail += 1
                continue
            url = f"{TORAPPU_ENDPOINT}/assets/char_avatar/{char_id}.png"
            save_path = op_icon_dir / f"{op['name']}.png"
            result = download_image(url, save_path)
            if result:
                dl_new += 1
            elif save_path.exists():
                dl_skip += 1
            else:
                dl_fail += 1
        print(f"  干员图标: {dl_new} 新下载, {dl_skip} 已存在, {dl_fail} 失败")

        print("下载装备图标...")
        dl_new, dl_skip, dl_fail = 0, 0, 0
        for eq in equipment:
            filename = f"卫戍协议_道具_{eq['name']}.png"
            url = get_prts_media_url(filename)
            save_path = eq_icon_dir / f"{eq['name']}.png"
            result = download_image(url, save_path)
            if result:
                dl_new += 1
            elif save_path.exists():
                dl_skip += 1
            else:
                dl_fail += 1
        print(f"  装备图标: {dl_new} 新下载, {dl_skip} 已存在, {dl_fail} 失败")
    else:
        print("跳过图标下载 (--skip-icons)")

    # 清理临时字段
    for op in operators:
        op.pop("_charId", None)

    # 7. 输出 JSON
    operators_path = data_dir / "operators.json"
    equipment_path = data_dir / "equipment.json"

    with open(operators_path, "w", encoding="utf-8") as f:
        json.dump({"operators": operators}, f, ensure_ascii=False, indent=2)

    with open(equipment_path, "w", encoding="utf-8") as f:
        json.dump({"equipment": equipment}, f, ensure_ascii=False, indent=2)

    print(f"\n完成！")
    print(f"  {operators_path.resolve()} ({operators_path.stat().st_size / 1024:.1f} KB)")
    print(f"  {equipment_path.resolve()} ({equipment_path.stat().st_size / 1024:.1f} KB)")
    print(f"\n概要: {len(operators)} 名干员, {len(equipment)} 件装备")


if __name__ == "__main__":
    main()
