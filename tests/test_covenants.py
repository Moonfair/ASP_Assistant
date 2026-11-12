import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("fetch_spdatabase", Path(__file__).resolve().parents[1] / "data/fetch_spdatabase.py")
fetcher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fetcher)


class CovenantCatalogTests(unittest.TestCase):
    def test_exports_activation_templates_harmony_bonus_and_transformation_pairing(self):
        bonds = {
            key: {"name": name, "bondType": kind}
            for key, name, kind in [("yan", "炎", "SEASON"), ("harm", "调和", "REGULAR"),
                                    ("solo", "独行", "REGULAR"), ("skill", "绝技", "REGULAR")]
        }
        season = {
            "bondInfoDict": {
                "yan": {"activeCount": 3, "activeConditionTemplate": "count_threshold_upward"},
                "harm": {"activeCount": 1, "activeConditionTemplate": "count_threshold_upward", "effectId": "bonus"},
                "solo": {"activeCount": 1, "activeConditionTemplate": "count_threshold_downward", "activeParamList": ["1", "2"]},
                "skill": {"activeCount": 2, "activeConditionTemplate": "count_threshold_upward_golden"},
            },
            "effectBuffInfoDataDict": {
                "bonus": [{"key": "other_bond_add_trigger_cnt", "blackboard": [{"key": "count", "value": 1.0}]}],
            },
            "trapShopChessDatas": {key: {"itemType": "EQUIP"} for key in ("shape", "blade", "other")},
            "trapChessDataDict": {
                "shape": {"effectId": "shape", "canGiveBond": True},
                "blade": {"effectId": "blade", "giveBondId": "yan"},
                "other": {"effectId": "other", "giveBondId": "old"},
            },
            "effectInfoDataDict": {"shape": {"effectName": "变形同构体"}, "blade": {"effectName": "炎国短刀"}},
        }
        result = fetcher.process_covenants(season, bonds)
        self.assertEqual({
            "炎": {"minimumCount": 3},
            "调和": {"minimumCount": 1, "coreActivationBonus": 1},
            "独行": {"minimumCount": 1, "maximumCount": 1},
            "绝技": {"minimumCount": 2, "requiresElite": True},
        }, result["activationRules"])
        self.assertEqual(["变形同构体"], result["jobChangeActivators"])
        self.assertEqual({"炎国短刀": "炎"}, result["jobChangeEquipment"])

    def test_icon_sources_follow_season_membership_and_upstream_icon_identifier(self):
        season = {"bondInfoDict": {"yanShip": {}, "preciShip": {}}}
        bonds = {
            "yanShip": {"name": "炎", "icon": "icon_yanShip"},
            "preciShip": {"name": "精准", "icon": "icon_preciShip"},
            "old": {"name": "旧赛季", "icon": "icon_old"},
        }
        sources = fetcher.covenant_icon_sources(season, bonds)
        self.assertEqual(set(sources), {"炎", "精准"})
        self.assertEqual(sources["炎"], "https://media.prts.wiki/c/c6/%E5%8D%AB%E6%88%8D%E5%8D%8F%E8%AE%AE%EF%BC%9A%E7%9B%9F%E7%BA%A6_icon_yanShip.png")
        self.assertTrue(sources["精准"].endswith("_icon_preciShip.png"))

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
