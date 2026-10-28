"""Pure fake-Console tests of the exact narrow production migration. No network."""
from copy import deepcopy
import json
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]
TEMP_ROOT = ROOT / '.cache/qa/equipment-migration'
TEMP_ROOT.mkdir(parents=True, exist_ok=True)
sys.path.insert(0, str(ROOT / 'Server/nakama/tools'))
from merge_equipment_ingots import AdminError, PATCH, COLLECTION, KEY, SYSTEM_USER_ID, merge_catalog, merge_console


def baseline():
    current = json.loads((ROOT / 'Server/nakama/data/duel_match3_item_defs.json').read_text(encoding='utf-8-sig'))
    for suffix, (_, old, _) in PATCH.items():
        current['items']['eq_t1_epic_' + suffix]['craft_ingot_n'] = old
    current['operator_metadata'] = {'custom': ['preserve', 1]}
    current['items']['admin_new_item'] = {'custom': True}
    current['items']['eq_t1_epic_legs']['admin_note'] = 'preserve'
    current['items']['eq_t1_epic_legs']['damage'] += 100
    return current


class FakeConsole:
    def __init__(self):
        self.row = dict(collection=COLLECTION, key=KEY, user_id=SYSTEM_USER_ID, version='exact-before',
                        permission_read=0, permission_write=0, value=baseline())
        self.writes = []
        self.conflict = False

    def read(self, collection, key):
        assert (collection, key) == (COLLECTION, KEY)
        return deepcopy(self.row)

    def write(self, collection, key, value, version):
        assert (collection, key) == (COLLECTION, KEY) and version == self.row['version'] and version != '*'
        self.writes.append((collection, key, deepcopy(value), version))
        if self.conflict:
            raise AdminError('CAS conflict')
        self.row['value'], self.row['version'] = deepcopy(value), 'exact-after'


class MigrationTests(unittest.TestCase):
    def test_exact_eight_fields_and_all_administrative_data_preserved(self):
        current = baseline(); original = deepcopy(current)
        result, changed = merge_catalog(current)
        self.assertEqual(len(changed), 8)
        self.assertEqual(current, original)
        for suffix, (_, old, new) in PATCH.items():
            item_id = 'eq_t1_epic_' + suffix
            self.assertEqual(result['items'][item_id]['craft_ingot_n'], new)
            result['items'][item_id]['craft_ingot_n'] = old
        self.assertEqual(result, original)

    def test_idempotent_current_canonical(self):
        current = json.loads((ROOT / 'Server/nakama/data/duel_match3_item_defs.json').read_text(encoding='utf-8-sig'))
        result, changed = merge_catalog(current)
        self.assertEqual(result, current); self.assertEqual(changed, [])

    def test_unknown_custom_target_price_is_not_overwritten(self):
        current = baseline(); current['items']['eq_t1_epic_legs']['craft_ingot_n'] = 777
        original = deepcopy(current)
        with self.assertRaises(AdminError): merge_catalog(current)
        self.assertEqual(current, original)

    def test_target_identity_missing_or_wrong_fails_closed(self):
        for field, value in [('kind', 'material'), ('tier', 2), ('quality', 'rare'), ('slot', 'Feet'),
                             ('craft_ingot_def', 'ingot_blue'), ('craft_recipe_id', 'custom'), ('craft_ingot_n', 180.0)]:
            current = baseline(); current['items']['eq_t1_epic_legs'][field] = value
            with self.assertRaises(AdminError, msg=field): merge_catalog(current)
        current = baseline(); del current['items']['eq_t1_epic_legs']
        with self.assertRaises(AdminError): merge_catalog(current)

    def test_dryrun_never_writes(self):
        console = FakeConsole(); result = merge_console(console)
        self.assertEqual(result['mode'], 'planned'); self.assertFalse(console.writes)

    def test_apply_saves_full_envelope_and_uses_one_exact_cas(self):
        console = FakeConsole(); before = deepcopy(console.row)
        with tempfile.TemporaryDirectory(dir=TEMP_ROOT) as directory:
            backup = Path(directory) / 'backup.json'
            result = merge_console(console, backup, True)
            self.assertEqual(json.loads(backup.read_text(encoding='utf-8')), before)
            self.assertEqual(result['mode'], 'applied'); self.assertEqual(len(console.writes), 1)
            result = merge_console(console, backup, True)
            self.assertEqual(result['mode'], 'unchanged'); self.assertEqual(len(console.writes), 1)

    def test_missing_or_existing_backup_refuses_write(self):
        console = FakeConsole()
        with self.assertRaises(AdminError): merge_console(console, apply=True)
        with tempfile.TemporaryDirectory(dir=TEMP_ROOT) as directory:
            backup = Path(directory) / 'backup.json'; backup.write_text('preserve', encoding='utf-8')
            with self.assertRaises(FileExistsError): merge_console(console, backup, True)
            self.assertEqual(backup.read_text(encoding='utf-8'), 'preserve')
        self.assertFalse(console.writes)

    def test_version_conflict_never_retries(self):
        console = FakeConsole(); console.conflict = True; before = deepcopy(console.row)
        with tempfile.TemporaryDirectory(dir=TEMP_ROOT) as directory:
            with self.assertRaises(AdminError): merge_console(console, Path(directory) / 'backup.json', True)
        self.assertEqual(len(console.writes), 1); self.assertEqual(console.row, before)

    def test_absent_wildcard_owner_or_permissions_never_write(self):
        for field, value in [('version', '*'), ('version', ''), ('user_id', 'other'), ('permission_read', 2), ('permission_write', 1)]:
            console = FakeConsole(); console.row[field] = value
            with self.assertRaises(AdminError): merge_console(console)
            self.assertFalse(console.writes)


if __name__ == '__main__':
    unittest.main()
