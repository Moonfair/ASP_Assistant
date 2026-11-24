"""Build the whitelist shared by desktop and server, and its full human-readable catalog."""
import argparse
import csv
import json
from pathlib import Path

from quick_chat.refresh_enemies import LEDGER, REPO, REV

ROOT = Path(__file__).resolve().parents[1]
RUNTIME = ROOT / "sites/asp-lineup-hub/server/quick-chat.json"
DOC = ROOT / "docs/quick-chat-catalog.md"


def load(path):
    return json.loads(path.read_bytes())


def names(file, key):
    return [row["name"] for row in load(ROOT / "data" / (file + ".json"))[key]]


def unique(values):
    return list(dict.fromkeys(values))


def validate(items, *, templates=False):
    if not items or len(set(items)) != len(items):
        raise ValueError(f"Empty or duplicate entries: {items}")
    for item in items:
        if (not item or item != item.strip() or not item.isprintable()
                or item.count("□") != int(templates)):
            raise ValueError(f"Invalid chat entry: {item!r}")


def build():
    phrases = load(ROOT / "scripts/quick_chat/phrases.json")
    template_groups = phrases["templateGroups"]
    templates = [t for group in template_groups.values() for t in group]
    validate(templates, templates=True)
    if templates[0] != "当心□！":
        raise ValueError("Default template must be 当心□！")
    base = names("operators", "operators")
    selected = names("self-selected-operators", "operators")
    battle = load(ROOT / "data/battle_operator_names.json")
    covenants = load(ROOT / "data/covenants.json")
    with LEDGER.open(encoding="utf-8", newline="") as stream:
        enemies = list(csv.DictReader(stream, delimiter="\t"))
    bosses = unique(row["name"] for row in enemies if "boss:" in row["sources"])
    groups = {
        "干员": unique([*base, *selected, *battle.values()]),
        "装备": names("equipment", "equipment"),
        "盟约": covenants["coreCovenants"] + covenants["additionalCovenants"],
        "敌人": unique(bosses + [row["name"] for row in enemies]),
        "策略": names("strategies", "strategies"),
        **phrases["groups"],
    }
    for words in groups.values():
        validate(words)
    catalog = {"templates": templates, "templateGroups": template_groups, "groups": groups}
    n_core = len(covenants["coreCovenants"])
    sources = [
        f"- **干员**：取 [基础表](../data/operators.json) 的 {len(base)} 项、"
        f"[自选表](../data/self-selected-operators.json) 的 {len(selected)} 项及"
        f"[战斗名称表](../data/battle_operator_names.json) 的 {len(battle)} 个 ID 的显示名并集。"
        "保留异格、职业后缀和协防专名，不把头像文件名、精锐状态或数值等级当作新名字。"
        "进入词库不表示本局必定可招募。",
        "- **装备、盟约、策略**：取 [装备表](../data/equipment.json) 的命名装备（含合成品）、"
        f"[盟约表](../data/covenants.json) 的 {n_core} 个核心及 {len(groups['盟约']) - n_core} 个附加盟约、"
        "[策略表](../data/strategies.json) 的全部策略名。转职别名、图标路径不另立词条。",
        f"- **敌人**：固定到[国服游戏数据镜像版本](https://github.com/{REPO}/commit/{REV})，"
        f"并非笼统收录全泰拉图鉴。从 `activity_table.json` 的 `act3autochess/V3_1` "
        "取特训池、附带敌人、奖励、技能效果、教学配置等处的敌人 ID；按本季领袖 ID 关联全局领袖表。"
        "再扫描整个本季关卡目录（含普通波次、战场子图、领袖、隐秘核心、逃脱模板）和配置引用的旧教学关卡，"
        "并递归补齐敌人技能/天赋参数中点名的衍生单位。"
        f"共 {len(enemies)} 个 ID；{sum('boss:' in row['sources'] for row in enemies)} 个领袖配置对应 "
        f"{len(bosses)} 个不同显示名。以游戏 `enemy_handbook_table.json` 的中文名合并同名变体，"
        "不根据图鉴互链扩张到别的活动。词库是配置引用的上界，含机制、占位与友方借用单位，"
        "不是某一难度的刷怪表。"
        "可按 [ID 及命中来源](../scripts/quick_chat/enemies.tsv) 核对。"
        "PRTS 也提供游戏[活动表](https://torappu.prts.wiki/gamedata/latest/excel/activity_table.json)"
        "和[敌人名称表](https://torappu.prts.wiki/gamedata/latest/excel/enemy_handbook_table.json)；"
        "[PRTS 盟约记录·敌方资料](https://prts.wiki/w/卫戍协议·规程/PRTS盟约记录#敌方资料) 可交叉查阅。",
        "- **附加词与句式**：[手编源](../scripts/quick_chat/phrases.json)。特训类型取本季九类机制；"
        "方位、战术用于报点和分工；趣味词与短句用于魂系留言式玩笑，不是游戏内实体。",
    ]
    note = [
        "# 卫戍协议 · 组队快捷喊话词库",
        "",
        "适用：**规程（act3autochess / V3_1）**。一个句式搭配一个白名单词；"
        "例如「当心□！」＋「假想敌：胄」→「当心假想敌：胄！」。",
        "",
        f"共有 **{len(templates)} 个句式、{len(groups)} 个固定词类、{sum(map(len, groups.values()))} 个分类词条**"
        "（同类精确去重；按类别计数）。下文逐项列出，与运行时白名单同源。",
        "",
        "另有动态页签 **房间昵称**：列出当前对局中自己和队友的已知昵称（不要求被点名者安装助手）。"
        "昵称也参与“全部”的模糊搜索；同名只列一次。人员变动后即时随房间同步，"
        "已离房的昵称不再可选，与之相关的最近喊话保留但不能重发。昵称不计入固定词条数。",
        "",
        "## 来源与收录口径",
        "",
        *sources,
        "",
        "## 句式",
        "",
    ]
    for name, items in template_groups.items():
        note.append(f"- **{name}（{len(items)}）**：" + "、".join(f"`{t}`" for t in items))
    note.extend(["", "## 可填入的词", ""])
    for name, items in groups.items():
        note.extend([f"### {name}（{len(items)}）", ""])
        for i in range(0, len(items), 8):
            note.append("- " + "、".join(f"`{item}`" for item in items[i:i+8]))
        note.append("")
    note.extend([
        "## 维护与复现",
        "",
        "从仓库根目录执行（Python 3.10+，标准库，无须启动客户端）：",
        "",
        "```sh",
        "python scripts/export_quick_chat.py          # 根据已入库的源离线生成 JSON 和本文档",
        "python scripts/export_quick_chat.py --check  # 离线检查两份发布产物是否同步",
        "python -m unittest discover -s tests -p test_quick_chat_catalog.py",
        "```",
        "",
        "要从上游独立复核敌人来源，运行 `python scripts/quick_chat/refresh_enemies.py --check`。"
        "首次会下载固定版本的游戏配置和关卡（缓存到忽略的 artifacts/）；"
        "更新赛季时调整脚本的版本与关卡树指纹，去掉 --check 重建敌人索引，再运行上述导出和测试。"
        "部署时只需一份[共享白名单](../sites/asp-lineup-hub/server/quick-chat.json)，不需要爬虫、外网或索引。",
    ])
    return {RUNTIME: json.dumps(catalog, ensure_ascii=False, indent=2) + "\n",
            DOC: "\n".join(note) + "\n"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    for path, content in build().items():
        if args.check:
            if not path.is_file() or path.read_text(encoding="utf-8") != content:
                raise SystemExit(f"Stale output: {path.relative_to(ROOT)}")
        else:
            path.write_text(content, encoding="utf-8", newline="\n")
        print(path.relative_to(ROOT))


if __name__ == "__main__":
    main()
