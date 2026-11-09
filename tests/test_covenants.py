import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("fetch_spdatabase", Path(__file__).resolve().parents[1] / "data/fetch_spdatabase.py")
fetcher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fetcher)


class CovenantCatalogTests(unittest.TestCase):
    def test_includes_rosterless_covenants_and_excludes_other_seasons(self):
        season = {"bondInfoDict": {"yan": {}, "skill": {"chessIdList": []}}}
        global_bonds = {
            "yan": {"name": "炎", "bondType": "SEASON"},
            "skill": {"name": "绝技", "bondType": "REGULAR"},
            "old": {"name": "拉特兰", "bondType": "SEASON"},
        }
        self.assertEqual({"coreCovenants": ["炎"], "additionalCovenants": ["绝技"]},
                         fetcher.process_covenants(season, global_bonds))

    def test_mode_pool_is_derived_from_active_season_bonds(self):
        season = {
            "bondInfoDict": {"yan": {}, "egir": {}, "precision": {}},
            "modeDataDict": {
                "standard": {"name": "标准模拟", "activeBondIdList": ["yan", "precision", "old"]},
                "single": {"name": "规程等级7", "activeBondIdList": ["yan", "egir", "precision"],
                           "groupList": ["GROUP_SINGLE"]},
            },
        }
        global_bonds = {
            "yan": {"name": "炎", "bondType": "SEASON"},
            "egir": {"name": "阿戈尔", "bondType": "SEASON"},
            "precision": {"name": "精准", "bondType": "REGULAR"},
            "old": {"name": "拉特兰", "bondType": "SEASON"},
        }
        self.assertEqual(
            {
                "标准模拟": {"coreCovenants": ["炎"], "additionalCovenants": ["精准"]},
                "规程等级7": {"coreCovenants": ["炎", "阿戈尔"], "additionalCovenants": ["精准"],
                             "selectCoreGroup": True},
            },
            fetcher.process_covenants(season, global_bonds)["modePools"],
        )


if __name__ == "__main__":
    unittest.main()
