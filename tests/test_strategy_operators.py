import importlib.util
import json
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "data"
spec = importlib.util.spec_from_file_location("fetch_spdatabase", DATA / "fetch_spdatabase.py")
fetcher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fetcher)


class StrategyOperatorTests(unittest.TestCase):
    def test_exports_strategy_chess_and_map_units_without_unreferenced_hidden_chess(self):
        characters = {
            "char_reward": {"name": "策略奖励"},
            "char_other": {"name": "未使用单位"},
            "char_605_cmedic": {"name": "预备干员-医疗"},
            "char_613_acmedc": {"name": "Touch"},
        }
        season = {
            "charChessDataDict": {
                "reward": {"upgradeChessId": "reward_elite", "garrisonIds": ["g1"]},
                "reward_elite": {"garrisonIds": ["g2"]},
                "unused": {"upgradeChessId": "unused_elite"},
            },
            "charShopChessDatas": {
                "reward": {"charId": "char_reward", "isHidden": True, "chessLevel": 5},
                "unused": {"charId": "char_other", "isHidden": True, "chessLevel": 3},
            },
            "garrisonDataDict": {
                "g1": {"eventTypeDesc": "作战能力", "garrisonDesc": "普通特质"},
                "g2": {"eventTypeDesc": "作战能力", "garrisonDesc": "精锐特质"},
            },
            "bandDataListDict": {
                "band_mech": {"effectId": "reward_effect"},
                "band_amedic": {"effectId": "medical_effect", "bandDesc": "【外勤医疗】\\n<@ba.vup>战斗开始时</>替换"},
            },
            "effectBuffInfoDataDict": {
                "reward_effect": [{
                    "key": "preparation_start_gain_chess_from_round",
                    "blackboard": [{"key": "type", "valueStr": "char"}, {"key": "chess", "valueStr": "reward"}],
                }],
                "medical_effect": [
                    {"key": "auto_chess_change_map", "blackboard": [
                        {"key": "char_605_cmedic#1", "value": 1},
                        {"key": "char_605_cmedic#2", "value": 1},
                        {"key": "char_613_acmedc#1", "value": 0},
                    ]},
                    {"key": "auto_chess_change_map", "blackboard": [
                        {"key": "char_613_acmedc#1", "value": 1},
                    ]},
                ],
            },
        }
        operators = fetcher.process_operators(characters, season, {})
        self.assertEqual(["策略奖励", "预备干员-医疗", "Touch"], [op["name"] for op in operators])
        self.assertTrue(all(op["strategyOnly"] for op in operators))
        self.assertEqual([5, 0, 0], [op["tier"] for op in operators])
        self.assertEqual("精锐特质", operators[0]["elite"]["traits"][0]["traitDescription"])
        self.assertEqual("战斗开始时替换", operators[1]["normal"]["traits"][0]["traitDescription"])
        self.assertEqual([], operators[2]["elite"]["traits"])

    def test_shipped_catalogs_include_strategy_operators_and_portraits(self):
        operators = json.loads((DATA / "operators.json").read_bytes())["operators"]
        web = ROOT / "sites" / "asp-lineup-hub" / "web"
        catalog = json.loads((web / "catalog.json").read_bytes())["operators"]
        portraits = json.loads((web / "portraits.json").read_bytes())
        metadata = json.loads((DATA / "source_metadata.json").read_bytes())
        self.assertEqual(len(operators), metadata["operatorCount"])
        for name in ("“暴躁铁皮”", "盟约·辅助干员", "预备干员-医疗", "Touch"):
            with self.subTest(name=name):
                matches = [op for op in operators if op["name"] == name]
                self.assertEqual(1, len(matches))
                self.assertTrue(matches[0]["strategyOnly"])
                self.assertTrue((DATA / matches[0]["iconPath"]).is_file())
                self.assertEqual(matches[0]["tier"], catalog[name]["tier"])
                self.assertIn(Path(catalog[name]["image"]).stem, portraits)
