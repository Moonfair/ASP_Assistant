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

try:
    from PIL import Image as _PilImage
    _PIL_AVAILABLE = True
except ImportError:
    _PIL_AVAILABLE = False

# ── 常量 ────────────────────────────────────────────────────────────────────

TORAPPU_ENDPOINT = "https://torappu.prts.wiki"
ARCHIVE_ENDPOINT = "https://static.prts.wiki/app/spdatabase"
MEDIA_ENDPOINT = "https://media.prts.wiki"

# Boss data sources (ArknightsGameData / ArknightsGameResource on GitHub)
ARKGAMEDATA_HANDBOOK_URL = (
    "https://raw.githubusercontent.com/Kengxxiao/ArknightsGameData"
    "/master/zh_CN/gamedata/excel/enemy_handbook_table.json"
)
YUANYAN_ENEMY_BASE_URL = (
    "https://raw.githubusercontent.com/yuanyan3060/ArknightsGameResource"
    "/main/enemy"
)

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

# 预备干员/协议特供棋子，不会出现在 ban 选角色界面
BAN_SCREEN_EXCLUDE_PREFIXES = ("预备干员-",)
BAN_SCREEN_EXCLUDE_SUFFIXES = ("(卫戍协议)",)

# 手动指定为"转职装备"的装备名称（用于名称中不含盟约名称但属于转职装备的情况，如维多利亚系列）
MANUAL_JOB_CHANGE_EQUIPMENTS: list[str] = ["维氏重锤","战栗维式重锤","坚固维式重锤","加速维式重锤","灼燃维式重锤"]

# 赛季配置
SP_SEASONS = {
    1: {
        "seasonCode": "act2autochess",
        "name": "盟约 下半 (Alliance 2nd Phase)",
        "mode": "ACTIVITY_SEASON",
        "equipIconMap": {
            "盟约之币": "幸运硬币",
            "奥术法阵": "口袋法阵",
            "骑士储蓄罐": "竞技投币机",
        },
    },
    2: {
        "seasonCode": "act1autochess",
        "name": "盟约 (Alliance)",
        "mode": "ACTIVITY_SEASON_ARC",
        "archiveFile": "alliance_1st.json",
        "equipIconMap": {
            # "数据中的名称": "Wiki图片中的名称",
        },
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
    # if save_path.exists():
    #     return False
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
    """从棋子条目构建特质列表（只保留第一条）"""
    for gar_id in chess_entry.get("garrisonIds", []):
        gar = garrison_dict.get(gar_id)
        if gar:
            return [{
                "traitType": gar["eventTypeDesc"],
                "traitDescription": strip_rich_text(gar["garrisonDesc"]),
            }]
    return []


def classify_bonds(chess_entry: dict, bond_info_dict: dict) -> tuple[list[str], list[str]]:
    """分类盟约为核心盟约列表和附加盟约列表"""
    core_covenants = []
    additional_covenants = []

    for bond_id in chess_entry.get("bondIds", []):
        bond = bond_info_dict.get(bond_id)
        if not bond:
            continue
        bond_name = bond["name"]
        bond_type = bond["bondType"]

        if bond_type == "SEASON":
            core_covenants.append(bond_name)
        elif bond_type == "REGULAR" and bond_name not in BOND_BAN:
            additional_covenants.append(bond_name)

    return core_covenants, additional_covenants


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
        if shop_entry["isHidden"]:
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
        core_covenants, additional_covenants = classify_bonds(chess, bond_info_dict)

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
            "coreCovenants": core_covenants,
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


# ── 图片后处理 ────────────────────────────────────────────────────────────────

def apply_white_background(img: "_PilImage.Image") -> "_PilImage.Image":
    """将透明背景合成为白色，返回完全不透明的 RGBA 图像。"""
    img = img.convert("RGBA")
    white_bg = _PilImage.new("RGBA", img.size, (255, 255, 255, 255))
    return _PilImage.alpha_composite(white_bg, img)


def apply_white_background_to_dir(skin_dir: Path) -> None:
    """对 skin_dir 目录内所有 PNG 文件原地应用白色背景。"""
    if not _PIL_AVAILABLE:
        print("  [ERROR] 需要 Pillow 库：pip install Pillow")
        return

    pngs = list(skin_dir.glob("*.png"))
    if not pngs:
        print(f"  [WARN] 目录中无 PNG 文件: {skin_dir}")
        return

    for path in pngs:
        img = _PilImage.open(path)
        img = apply_white_background(img)
        img.save(path)

    print(f"  白色背景已应用: {len(pngs)} 张图片 ({skin_dir})")


# ── 皮肤头像处理 ──────────────────────────────────────────────────────────────

def process_skin_avatars(
    operators: list[dict],
    skin_table: dict,
    data_dir: Path,
) -> dict[str, str]:
    """为干员池中的干员下载所有皮肤头像，返回 {filename: operator_name} 映射。

    跳过 BAN_SCREEN_EXCLUDE 中的特殊干员（预备干员/卫戍协议棋子）。
    """
    char_skins: dict = skin_table.get("charSkins", {})

    char_id_to_name: dict[str, str] = {}
    for op in operators:
        name = op["name"]
        if any(name.startswith(p) for p in BAN_SCREEN_EXCLUDE_PREFIXES):
            continue
        if any(name.endswith(s) for s in BAN_SCREEN_EXCLUDE_SUFFIXES):
            continue
        char_id = op.get("_charId", "")
        if char_id:
            char_id_to_name[char_id] = name

    skin_dir = data_dir / "icons" / "skin_avatars"
    skin_dir.mkdir(parents=True, exist_ok=True)

    avatar_map: dict[str, str] = {}
    dl_new, dl_skip, dl_fail = 0, 0, 0

    for skin_id, skin in char_skins.items():
        char_id = skin.get("charId", "")
        if char_id not in char_id_to_name:
            continue

        avatar_id = skin.get("avatarId", "")
        if not avatar_id:
            continue

        op_name = char_id_to_name[char_id]
        filename = f"{avatar_id}.png"
        safe_filename = filename.replace("#", "_")

        avatar_map[safe_filename] = op_name

        url_avatar = urllib.parse.quote(avatar_id, safe="")
        url = f"{TORAPPU_ENDPOINT}/assets/char_avatar/{url_avatar}.png"
        save_path = skin_dir / safe_filename

        result = download_image(url, save_path)
        if result:
            dl_new += 1
            if _PIL_AVAILABLE:
                img = _PilImage.open(save_path)
                img = apply_white_background(img)
                img.save(save_path)
        elif save_path.exists():
            dl_skip += 1
        else:
            dl_fail += 1

    print(f"  皮肤头像: {dl_new} 新下载, {dl_skip} 已存在, {dl_fail} 失败")
    print(f"  映射条目: {len(avatar_map)} (覆盖 {len(char_id_to_name)} 名干员)")

    return avatar_map


# ── 特训敌人类型处理 ──────────────────────────────────────────────────────────

# 类型代码 → 中文名（与游戏内 OCR 识别到的 "特训敌人·XXX" 后缀对应）
ENEMY_TYPE_NAMES: dict[str, str] = {
    "DOT":        "持续",
    "ELEMENT":    "元素",
    "FLY":        "飞行",
    "INVISIBLE":  "隐匿",
    "REFLECTION": "折射",
    "SPECIAL":    "特异",
    "TIMES":      "频次",
}

# 类型代码 → PRTS Wiki 图标文件名
# URL 形如: https://media.prts.wiki/{md5[0]}/{md5[:2]}/特训敌人_飞行.png
ENEMY_TYPE_ICON_FILES: dict[str, str] = {
    "DOT":        "特训敌人_持续.png",
    "ELEMENT":    "特训敌人_元素.png",
    "FLY":        "特训敌人_飞行.png",
    "INVISIBLE":  "特训敌人_隐匿.png",
    "REFLECTION": "特训敌人_折射.png",
    "SPECIAL":    "特训敌人_特异.png",
    "TIMES":      "特训敌人_频次.png",
}


def process_enemy_types(season_data: dict, enemy_name_map: dict[str, str] | None = None) -> dict:
    """
    从赛季数据的 specialEnemyInfoDict 中提取特训敌人类型信息。

    返回 dict keyed by typeCode，每条记录包含：
      - typeCode / typeName
      - firstHalfVariants: isInFirstHalf=True 的代表敌人列表（iconPath 已填充）
      - secondHalfVariants: isInFirstHalf=False 的代表敌人列表
    """
    spec_dict: dict = season_data.get("specialEnemyInfoDict", {})
    enemy_name_map = enemy_name_map or {}

    types: dict[str, dict] = {}

    def pick_enemy_name(enemy_id: str, e: dict) -> str:
        mapped_name = enemy_name_map.get(enemy_id)
        if mapped_name:
            return mapped_name

        # The upstream schema is not fully stable. Try common name fields first.
        for key in ("enemyName", "name", "displayName", "enemyDisplayName"):
            value = e.get(key)
            if isinstance(value, str) and value.strip():
                return value.strip()

        # Some payloads may nest enemy metadata in a sub-object.
        for node_key in ("enemyData", "enemy", "enemyInfo"):
            node = e.get(node_key)
            if not isinstance(node, dict):
                continue
            for key in ("enemyName", "name", "displayName"):
                value = node.get(key)
                if isinstance(value, str) and value.strip():
                    return value.strip()

        return ""

    for enemy_id, entry in spec_dict.items():
        if not entry:
            continue
        type_code: str = entry.get("type", "")
        if not type_code:
            continue

        type_name = ENEMY_TYPE_NAMES.get(type_code, type_code)
        is_first_half: bool = bool(entry.get("isInFirstHalf", True))
        weight: int = int(entry.get("randomWeight", 1))

        if type_code not in types:
            types[type_code] = {
                "typeCode":     type_code,
                "typeName":     type_name,
                "typeIconPath": f"icons/enemy_types/{type_code}.png",
                "firstHalfVariants":  [],
                "secondHalfVariants": [],
            }

        variant = {
            "specialEnemyId": enemy_id,
            "enemyName":       pick_enemy_name(enemy_id, entry),
            "iconPath":        f"icons/enemies/{enemy_id}.png",
            "weight":          weight,
        }

        if is_first_half:
            types[type_code]["firstHalfVariants"].append(variant)
        else:
            types[type_code]["secondHalfVariants"].append(variant)

    # 按 weight 降序排列，最常见的放前面
    for t in types.values():
        t["firstHalfVariants"].sort(key=lambda v: -v["weight"])
        t["secondHalfVariants"].sort(key=lambda v: -v["weight"])

    return types


def collect_enemy_ids_for_icons(enemy_types: dict) -> set[str]:
    """收集所有需要下载头像的敌人 ID。"""
    ids: set[str] = set()
    for t in enemy_types.values():
        for v in t["firstHalfVariants"] + t["secondHalfVariants"]:
            ids.add(v["specialEnemyId"])
    return ids


def download_enemy_icons(enemy_ids: set[str], data_dir: Path) -> None:
    """从 yuanyan3060/ArknightsGameResource 下载特训小怪头像。"""
    icon_dir = data_dir / "icons" / "enemies"
    icon_dir.mkdir(parents=True, exist_ok=True)

    dl_new, dl_skip, dl_fail = 0, 0, 0
    for enemy_id in sorted(enemy_ids):
        save_path = icon_dir / f"{enemy_id}.png"
        if save_path.exists():
            dl_skip += 1
            continue
        url = f"{YUANYAN_ENEMY_BASE_URL}/{enemy_id}.png"
        if download_image(url, save_path):
            dl_new += 1
        else:
            dl_fail += 1

    print(f"  特训小怪头像: {dl_new} 新下载, {dl_skip} 已存在, {dl_fail} 失败")


def download_enemy_type_icons(data_dir: Path) -> None:
    """从 PRTS Media Wiki 下载 7 种特训敌人类型图标。
    保存为 data/icons/enemy_types/{typeCode}.png。
    """
    icon_dir = data_dir / "icons" / "enemy_types"
    icon_dir.mkdir(parents=True, exist_ok=True)

    dl_new, dl_skip, dl_fail = 0, 0, 0
    for type_code, filename in ENEMY_TYPE_ICON_FILES.items():
        save_path = icon_dir / f"{type_code}.png"
        if save_path.exists():
            dl_skip += 1
            continue
        url = get_prts_media_url(filename)
        if download_image(url, save_path):
            dl_new += 1
        else:
            dl_fail += 1

    print(f"  类型图标: {dl_new} 新下载, {dl_skip} 已存在, {dl_fail} 失败")


# ── Boss（卫戍协议「领袖」池）处理 ────────────────────────────────────────────

# 与游戏内卫戍协议 / 盟约「领袖」表一致（同名多 phase 只保留主条目一条）
GARRISON_PROTOCOL_BOSS_IDS: frozenset[str] = frozenset(
    {
        "enemy_9013_acstmk",  # 假想敌：胄
        "enemy_9017_achunt",  # 假想敌：铳
        "enemy_9021_acduml",  # 假想敌：管
        "enemy_1521_dslily",  # 盐风主教昆图斯
        "enemy_2016_csphtm",  # 卢西恩，“猩红血钻”
        "enemy_9032_aclionk",  # 阿利斯泰尔，帝国余晖（盟约）
        "enemy_9033_acdeer",  # “萨米的意志”（盟约）
    }
)

# 与游戏内「领袖」表展示顺序一致（非 enemyIndex 字母序）
GARRISON_BOSS_ORDER: tuple[str, ...] = (
    "enemy_9013_acstmk",
    "enemy_9017_achunt",
    "enemy_9021_acduml",
    "enemy_1521_dslily",
    "enemy_2016_csphtm",
    "enemy_9032_aclionk",
    "enemy_9033_acdeer",
)
_GARRISON_ORDER_RANK: dict[str, int] = {eid: i for i, eid in enumerate(GARRISON_BOSS_ORDER)}

# 在 handbook 名称之外补充 OCR 常见简称（enemyId -> 额外 aliases）
BOSS_EXTRA_ALIASES: dict[str, list[str]] = {
    "enemy_9032_aclionk": ["帝国余晖"],
}


def process_bosses(handbook_data: dict) -> list[dict]:
    """
    从 enemy_handbook_table.json 中提取卫戍协议「领袖」池：
    enemyLevel == "BOSS" 且 enemyId 属于 GARRISON_PROTOCOL_BOSS_IDS。
    """
    enemy_data: dict = handbook_data.get("enemyData", {})
    bosses = []

    for enemy_id, entry in enemy_data.items():
        if not entry:
            continue
        if enemy_id not in GARRISON_PROTOCOL_BOSS_IDS:
            continue
        if entry.get("enemyLevel") != "BOSS":
            continue
        name: str = (entry.get("name") or "").strip()
        if not name:
            continue

        ability_list = [
            a["text"]
            for a in (entry.get("abilityList") or [])
            if a.get("text")
        ]
        damage_types = entry.get("damageType") or []

        row = {
            "enemyId": enemy_id,
            "enemyIndex": entry.get("enemyIndex") or "",
            "name": name,
            "aliases": [],
            "description": entry.get("description"),
            "attackType": entry.get("attackType"),
            "damageTypes": damage_types,
            "abilityList": ability_list,
            "iconPath": f"icons/bosses/{enemy_id}.png",
        }
        extras = BOSS_EXTRA_ALIASES.get(enemy_id)
        if extras:
            for a in extras:
                if a not in row["aliases"]:
                    row["aliases"].append(a)
        bosses.append(row)

    bosses.sort(
        key=lambda b: (_GARRISON_ORDER_RANK.get(b.get("enemyId") or "", 999), b.get("enemyId") or "")
    )
    return bosses


def build_enemy_name_map(handbook_data: dict) -> dict[str, str]:
    """从 enemy_handbook_table.json 构建 enemyId -> 中文名 映射。"""
    enemy_data: dict = handbook_data.get("enemyData", {})
    name_map: dict[str, str] = {}
    for enemy_id, entry in enemy_data.items():
        if not enemy_id or not entry:
            continue
        name = (entry.get("name") or "").strip()
        if name:
            name_map[enemy_id] = name
    return name_map


def merge_extra_bosses_from_disk(bosses: list[dict], bosses_path: Path) -> list[dict]:
    """
    保留 bosses.json 中已有、但本次 handbook 未返回的条目；
    仅合并 enemyId 在 GARRISON_PROTOCOL_BOSS_IDS 内的行，避免塞回全图鉴 Boss。
    """
    if not bosses_path.exists():
        return bosses
    try:
        with open(bosses_path, encoding="utf-8") as f:
            old = json.load(f).get("bosses") or []
    except Exception:
        return bosses
    new_ids = {b.get("enemyId") for b in bosses if b.get("enemyId")}
    merged = list(bosses)
    for b in old:
        eid = b.get("enemyId")
        if not eid or eid in new_ids:
            continue
        if eid not in GARRISON_PROTOCOL_BOSS_IDS:
            continue
        merged.append(b)
        new_ids.add(eid)
    merged.sort(
        key=lambda b: (_GARRISON_ORDER_RANK.get(b.get("enemyId") or "", 999), b.get("enemyId") or "")
    )
    return merged


def download_boss_icons(bosses: list[dict], data_dir: Path) -> None:
    """从 yuanyan3060/ArknightsGameResource 下载 boss 头像。"""
    icon_dir = data_dir / "icons" / "bosses"
    icon_dir.mkdir(parents=True, exist_ok=True)

    dl_new, dl_skip, dl_fail = 0, 0, 0
    for boss in bosses:
        enemy_id = boss["enemyId"]
        save_path = icon_dir / f"{enemy_id}.png"
        if save_path.exists():
            dl_skip += 1
            continue
        url = f"{YUANYAN_ENEMY_BASE_URL}/{enemy_id}.png"
        if download_image(url, save_path):
            dl_new += 1
        else:
            dl_fail += 1

    print(f"  Boss 头像: {dl_new} 新下载, {dl_skip} 已存在, {dl_fail} 失败")


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
    parser.add_argument("--skip-bosses",   action="store_true", help="跳过 Boss 数据获取")
    parser.add_argument("--skip-enemies", action="store_true", help="跳过特训敌人类型数据获取")
    parser.add_argument("--download-skins", action="store_true", help="下载皮肤头像并生成映射文件")
    parser.add_argument("--apply-mask", action="store_true",
                        help="对 skin_avatars/ 目录内所有现有 PNG 原地补白色背景（需要 Pillow）")
    args = parser.parse_args()

    # --apply-mask 是独立操作，不需要拉取网络数据
    if args.apply_mask:
        data_dir = Path(__file__).parent
        print("=== 皮肤头像后处理 ===")
        apply_white_background_to_dir(data_dir / "icons" / "skin_avatars")
        return

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
        equip_icon_map = season_cfg.get("equipIconMap", {})
        for eq in equipment:
            icon_name = equip_icon_map.get(eq["name"], eq["name"])
            filename = f"卫戍协议_道具_{icon_name}.png"
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

    handbook_data: dict | None = None
    if not args.skip_enemies or not args.skip_bosses:
        print("\n获取敌人图鉴数据 (enemy_handbook_table.json)...")
        handbook_data = fetch_json(ARKGAMEDATA_HANDBOOK_URL)

    # 7. 处理特训敌人类型数据
    if not args.skip_enemies:
        print("\n处理特训敌人类型数据...")
        enemy_name_map = build_enemy_name_map(handbook_data or {})
        enemy_types = process_enemy_types(season_data, enemy_name_map)
        enemy_ids_for_icons = collect_enemy_ids_for_icons(enemy_types)
        print(f"  已加载中文敌人名: {len(enemy_name_map)}")
        print(f"  类型数: {len(enemy_types)}, 敌人变体数: {sum(len(t['firstHalfVariants']) + len(t['secondHalfVariants']) for t in enemy_types.values())}")

        if not args.skip_icons:
            print("下载特训小怪头像...")
            download_enemy_icons(enemy_ids_for_icons, data_dir)
            print("下载特训敌人类型图标...")
            download_enemy_type_icons(data_dir)

        enemy_types_path = data_dir / "enemy_types.json"
        with open(enemy_types_path, "w", encoding="utf-8") as f:
            json.dump({"types": enemy_types}, f, ensure_ascii=False, indent=2)
        print(f"  {enemy_types_path.resolve()} ({enemy_types_path.stat().st_size / 1024:.1f} KB)")
    else:
        print("\n跳过特训敌人类型数据获取 (--skip-enemies)")

    # 8. 获取 Boss 数据
    if not args.skip_bosses:
        print("\n处理 Boss 数据...")
        bosses = process_bosses(handbook_data or {})
        bosses_path = data_dir / "bosses.json"
        bosses = merge_extra_bosses_from_disk(bosses, bosses_path)
        print(f"  Boss 数: {len(bosses)}")

        if not args.skip_icons:
            print("下载 Boss 头像...")
            download_boss_icons(bosses, data_dir)

        with open(bosses_path, "w", encoding="utf-8") as f:
            json.dump({"bosses": bosses}, f, ensure_ascii=False, indent=2)
        print(f"  {bosses_path.resolve()} ({bosses_path.stat().st_size / 1024:.1f} KB)")
    else:
        print("\n跳过 Boss 数据获取 (--skip-bosses)")

    # 8. 下载皮肤头像并生成映射
    if args.download_skins:
        print("\n获取皮肤数据 (skin_table.json)...")
        skin_table = fetch_json(
            f"{TORAPPU_ENDPOINT}/gamedata/latest/excel/skin_table.json"
        )
        print("下载皮肤头像...")
        avatar_map = process_skin_avatars(operators, skin_table, data_dir)

        avatar_map_path = data_dir / "skin_avatar_map.json"
        with open(avatar_map_path, "w", encoding="utf-8") as f:
            json.dump(avatar_map, f, ensure_ascii=False, indent=2)
        print(f"  映射已保存: {avatar_map_path.name} ({avatar_map_path.stat().st_size / 1024:.1f} KB)")

    # 清理临时字段
    for op in operators:
        op.pop("_charId", None)

    # 10. 输出 JSON
    operators_path = data_dir / "operators.json"
    equipment_path = data_dir / "equipment.json"

    with open(operators_path, "w", encoding="utf-8") as f:
        json.dump({"operators": operators}, f, ensure_ascii=False, indent=2)

    with open(equipment_path, "w", encoding="utf-8") as f:
        json.dump(
            {
                "manualJobChangeEquipments": MANUAL_JOB_CHANGE_EQUIPMENTS,
                "equipment": equipment,
            },
            f,
            ensure_ascii=False,
            indent=2,
        )

    print(f"\n完成！")
    print(f"  {operators_path.resolve()} ({operators_path.stat().st_size / 1024:.1f} KB)")
    print(f"  {equipment_path.resolve()} ({equipment_path.stat().st_size / 1024:.1f} KB)")
    print(f"\n概要: {len(operators)} 名干员, {len(equipment)} 件装备")


if __name__ == "__main__":
    main()
