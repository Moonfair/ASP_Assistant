import importlib.util
import json
from pathlib import Path
import unittest


DATA_DIR = Path(__file__).resolve().parents[1] / "data"
spec = importlib.util.spec_from_file_location("fetch_spdatabase", DATA_DIR / "fetch_spdatabase.py")
fetcher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fetcher)


class DescriptionTextTests(unittest.TestCase):
    def test_removes_style_and_keyword_tags_but_preserves_gameplay_and_timing(self):
        raw = "<战斗中>攻击力<@ba.vup>+30%</>；造成<@ba.vup><$ba.dt.burning2>灼燃损伤</></>，持续5秒\n<获得时>【炎】层数+1"
        self.assertEqual("<战斗中>攻击力+30%；造成灼燃损伤，持续5秒\n<获得时>【炎】层数+1",
                         fetcher.strip_rich_text(raw))

    def test_uses_upstream_static_text_for_all_current_operator_counters(self):
        cases = json.loads((Path(__file__).parent / "fixtures/runtime_descriptions.json").read_text(encoding="utf-8"))
        for case in cases:
            with self.subTest(name=case["name"], variant=case["variant"]):
                result = fetcher.build_traits(case["chess"], case["garrisons"])[0]
                self.assertEqual(fetcher.strip_rich_text(case["chess"]["desc"]), result["traitDescription"])
                self.assertNotRegex(result["traitDescription"], r"\{[^}]+\}|<[@$]")
                self.assertEqual(next(iter(case["garrisons"].values()))["eventTypeDesc"], result["traitType"])

    def test_preserves_existing_description_without_runtime_parameters(self):
        chess = {"garrisonIds": ["g1"], "desc": "另一个描述"}
        self.assertEqual([{"traitType": "单次叠加", "traitDescription": "<获得时>【炎】层数+8"}],
                         fetcher.build_traits(chess, {"g1": {"eventTypeDesc": "单次叠加", "garrisonDesc": "<获得时>【炎】层数<@ba.vup>+8</>"}}))

    def test_deposit_uses_accumulated_amount_instead_of_fabricating_a_live_balance(self):
        effects = {
            "deposit": {"effectName": "定期存折", "effectDesc": "<@ba.vdown>装备时销毁</>，获得{showtag_value_1}资金（持有时每回合<@ba.vup>+1</>）"},
            "gold": {"effectName": "定期存折", "effectDesc": "<@ba.vdown>装备时销毁</>，获得{showtag_value_1}资金（持有时每回合<@ba.vup>+2</>）"},
        }
        season = {
            "trapShopChessDatas": {"normal": {"itemType": "EQUIP", "itemLevel": 3, "goldenItemId": "golden"}},
            "trapChessDataDict": {"normal": {"effectId": "deposit"}, "golden": {"effectId": "gold"}},
            "effectInfoDataDict": effects,
            "effectBuffInfoDataDict": {key: [{"key": "equip_destory_gain_coin", "blackboard": []}] for key in effects},
        }
        result = fetcher.process_equipment(season)[0]
        self.assertEqual("装备时销毁，获得累计的资金（持有时每回合+1）", result["normal"]["effectDescription"])
        self.assertEqual("装备时销毁，获得累计的资金（持有时每回合+2）", result["elite"]["effectDescription"])

    def test_unknown_runtime_parameter_requires_a_supported_static_description(self):
        chess = {"garrisonIds": ["g1"], "desc": "仍含{showtag_value_1}"}
        with self.assertRaisesRegex(ValueError, "占位符"):
            fetcher.build_traits(chess, {"g1": {"eventTypeDesc": "整备能力", "garrisonDesc": "获得{showtag_value_1}资金"}})


if __name__ == "__main__":
    unittest.main()
