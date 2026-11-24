import csv
import json
import subprocess
import sys
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DEPLOYED = ROOT / "sites/asp-lineup-hub/server/quick-chat.json"
LEDGER = ROOT / "scripts/quick_chat/enemies.tsv"


def read_data(name):
    return json.loads((ROOT / "data" / (name + ".json")).read_bytes())


class QuickChatCatalogTests(unittest.TestCase):
    def setUp(self):
        self.assertTrue(DEPLOYED.is_file(), "The shared runtime lexicon hasn't been exported")
        self.catalog = json.loads(DEPLOYED.read_bytes())

    def test_every_template_can_be_completed_and_found_in_one_tab(self):
        templates = self.catalog["templates"]
        categorized = [t for group in self.catalog["templateGroups"].values() for t in group]
        self.assertEqual("当心□！", templates[0])
        self.assertCountEqual(templates, categorized)
        self.assertEqual(len(templates), len(set(templates)))
        self.assertTrue(all(t.count("□") == 1 for t in templates))

    def test_operator_coverage_includes_nonstandard_rosters(self):
        groups = self.catalog["groups"]
        self.assertEqual({row["name"] for source in ("operators", "self-selected-operators")
                          for row in read_data(source)["operators"]} |
                         set(read_data("battle_operator_names").values()), set(groups["干员"]))
        self.assertIn("预备干员-医疗", groups["干员"])

    def test_all_equipment_bonds_and_strategies_are_sendable(self):
        groups = self.catalog["groups"]
        covenants = read_data("covenants")
        self.assertCountEqual([i["name"] for i in read_data("equipment")["equipment"]], groups["装备"])
        self.assertCountEqual(covenants["coreCovenants"] + covenants["additionalCovenants"], groups["盟约"])
        self.assertCountEqual([i["name"] for i in read_data("strategies")["strategies"]], groups["策略"])

    def test_declared_enemies_include_waves_specials_bosses_and_summons(self):
        with LEDGER.open(encoding="utf-8", newline="") as stream:
            rows = list(csv.DictReader(stream, delimiter="\t"))
        self.assertEqual(len(rows), len({r["id"] for r in rows}))
        enemies = self.catalog["groups"]["敌人"]
        self.assertCountEqual({r["name"] for r in rows}, enemies)
        for word in ["源石虫", "假想敌：蚀裂", "假想敌：胄", "矩兽", "假想敌：弦", "淤困之茧"]:
            self.assertIn(word, enemies)
        self.assertEqual(16, sum("boss:" in r["sources"] for r in rows))

    def test_shipped_artifacts_are_reproducible_offline(self):
        check = subprocess.run([sys.executable, str(ROOT / "scripts/export_quick_chat.py"), "--check"],
                               cwd=ROOT, capture_output=True)
        self.assertEqual(0, check.returncode, check.stderr + check.stdout)


if __name__ == "__main__":
    unittest.main()
