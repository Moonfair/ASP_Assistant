import json
from pathlib import Path
import unittest

from test_strategy_operators import fetcher


class SelfSelectedOperatorTests(unittest.TestCase):
    def test_uses_main_and_hidden_powers_scoped_to_current_season(self):
        def character(name, nation=None, hidden=None, **overrides):
            return {
                "name": name, "rarity": "TIER_6", "isNotObtainable": False,
                "mainPower": {"nationId": nation}, "subPower": hidden, **overrides,
            }

        characters = {
            "char_fixed": character("固定干员", "yan"),
            "char_dual": character("双势力", "yan", [{"groupId": "pinus"}]),
            "char_outside": character("赛季外势力", "laterano"),
            "char_609_acguad": character("Sharp", isNotObtainable=True),
            "char_unavailable": character("不可获得", isNotObtainable=True),
        }
        season = {
            "diyChessDict": {"slot": "TIER_6"},
            "charShopChessDatas": {"fixed": {"charId": "char_fixed", "backupCharId": "char_609_acguad"}},
            "bondInfoDict": {"yan": {}, "kazi": {}, "empty": {}},
            "constData": {"fallbackBondId": "empty", "systemCustomChar": ["char_609_acguad"]},
        }
        bonds = {
            "yan": {"bondType": "SEASON", "name": "炎", "powerIdList": ["yan"]},
            "kazi": {"bondType": "SEASON", "name": "卡西米尔", "powerIdList": ["pinus"]},
            "lat": {"bondType": "SEASON", "name": "拉特兰", "powerIdList": ["laterano"]},
            "empty": {"bondType": "REGULAR", "name": "协防干员", "powerIdList": []},
        }
        operators = {op["_charId"]: op for op in fetcher.process_self_selected_operators(characters, season, bonds)}
        self.assertEqual({"char_dual", "char_outside", "char_609_acguad"}, operators.keys())
        self.assertEqual(["炎", "卡西米尔"], operators["char_dual"]["coreCovenants"])
        self.assertEqual(["协防干员"], operators["char_outside"]["additionalCovenants"])
        self.assertTrue(all(op["tier"] == 0 and op["selfSelected"] for op in operators.values()))

    def test_shipped_catalog_is_separate_and_has_portraits(self):
        root = Path(__file__).resolve().parents[1]
        base = json.loads((root / "data/operators.json").read_bytes())["operators"]
        optional = json.loads((root / "data/self-selected-operators.json").read_bytes())["operators"]
        self.assertTrue(optional)
        self.assertTrue({op["name"] for op in base}.isdisjoint(op["name"] for op in optional))
        self.assertTrue(all((root / "data" / op["iconPath"]).is_file() for op in optional))
