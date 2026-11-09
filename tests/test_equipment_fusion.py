import importlib.util
from pathlib import Path
import unittest


spec = importlib.util.spec_from_file_location(
    "fetch_spdatabase", Path(__file__).resolve().parents[1] / "data/fetch_spdatabase.py"
)
fetcher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fetcher)


class EquipmentFusionTests(unittest.TestCase):
    def test_mirrored_fusion_records_produce_one_formula_on_the_result(self):
        names = {"spear": "天马之枪", "helm": "天马之盔", "result": "天马之耀"}
        season = {
            "trapShopChessDatas": {
                key: {"itemType": "EQUIP", "itemLevel": 6} for key in names
            },
            "trapChessDataDict": {
                "spear": {"effectId": "spear", "canFuse": True, "fuseWith": "helm", "fuseResult": "result"},
                "helm": {"effectId": "helm", "canFuse": True, "fuseWith": "spear", "fuseResult": "result"},
                "result": {"effectId": "result", "isGolden": True},
            },
            "effectInfoDataDict": {key: {"effectName": name} for key, name in names.items()},
        }

        formulas = {item["name"]: item["fusionFormula"]
                    for item in fetcher.process_equipment(season) if "fusionFormula" in item}
        self.assertEqual({"天马之耀": "天马之枪 + 天马之盔"}, formulas)
