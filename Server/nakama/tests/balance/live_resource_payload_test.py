"""Verify live HUD balances on a disposable account, never on a real player.

Run on the Escape host from /opt/escape. Credentials stay in server memory.
Console fixture writes deliberately leave historical counters larger than the
spendable inventory. This tests responses, not crafting or sale transactions.
"""
import argparse
import base64
import importlib.util
import json
from pathlib import Path
from urllib.parse import quote
from urllib.request import Request, urlopen
import uuid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--url', default='https://prokrust-play.ru')
    args = parser.parse_args()
    root = Path('/opt/escape')
    spec = importlib.util.spec_from_file_location('storage_admin', root / 'tools/storage_admin.py')
    admin = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(admin)
    config = json.loads((root / 'secrets/server.yml').read_text())
    console = admin.Console('http://127.0.0.1:27351')
    token, uid, epoch = None, None, 0
    basic = 'Basic ' + base64.b64encode((config['socket']['server_key'] + ':').encode()).decode()
    username = 'resource_audit_' + uuid.uuid4().hex

    def request(method, path, value=None, auth=None):
        body = None if value is None else json.dumps(value).encode()
        req = Request(args.url + path, data=body, method=method,
                      headers={'Content-Type': 'application/json', 'Authorization': auth or 'Bearer ' + token})
        with urlopen(req, timeout=20) as response:
            data = response.read()
            return json.loads(data) if data else {}

    def rpc(name):
        response = request('POST', '/v2/rpc/' + name, json.dumps({'session_epoch': epoch}))
        value = json.loads(response['payload'])
        assert value.get('ok'), name + ' failed'
        return value

    def fixture(collection, key, value):
        path = '/v2/console/storage/' + '/'.join(quote(p, safe='') for p in (collection, key, uid))
        try:
            previous = console.call('GET', path)
            version = previous['version']
        except admin.ApiError as error:
            if error.status != 404:
                raise
            version = '*'
        console.call('PUT', path, {'collection': collection, 'key': key, 'user_id': uid,
            'value': json.dumps(value), 'version': version, 'permission_read': 1, 'permission_write': 0})

    try:
        console.login(config['console']['username'], config['console']['password'])
        session = request('POST', '/v2/account/authenticate/device?create=true&username=' + username,
                          {'id': 'resource-audit-' + uuid.uuid4().hex}, basic)
        token = session['token']
        claims = json.loads(base64.urlsafe_b64decode(token.split('.')[1] + '=='))
        uid = claims['uid']
        account = request('GET', '/v2/account')
        assert account['user']['id'] == uid and account['user']['username'] == username
        epoch = rpc('duel_session_claim')['session_epoch']
        fixture('duel_match3_progress', 'profile', {
            'xp': 0, 'level': 1, 'gold': 123, 'ore': 456, 'matter': 7, 'energy': 73,
            'ingots': 1000, 'tesseracts': 900, 'key_items': {'miner_key': 2, 'dark_key': 3}})
        cases = [([4, 3, 2, 3], 9, 3), ([0, 3, 0, 1], 3, 1), ([0, 0, 0, 0], 0, 0)]
        for counts, ingots, tesseracts in cases:
            ids = ['ingot_green', 'ingot_blue', 'ingot_purple', 'tesseract']
            fixture('duel_match3_character', 'sheet', {
                'inventory': [item if count else '' for item, count in zip(ids, counts)],
                'inventory_counts': counts, 'equipment': {}, 'workshop_slots': {}, 'learned_recipes': {}})
            resources = rpc('duel_player_resources_get')
            character = rpc('duel_character_get')
            assert resources['ingots'] == ingots and resources['tesseracts'] == tesseracts
            assert character['progression']['ingots'] == ingots
            assert resources['gold'] == 123 and resources['ore'] == 456 and resources['keys'] == 5
            print('PASS live resource/character RPCs: ingots=' + str(ingots) + ', tesseracts=' + str(tesseracts), flush=True)
    finally:
        try:
            if uid:
                console.call('DELETE', '/v2/console/account/' + uid + '?record_deletion=false')
                print('PASS disposable HUD test account and fixture storage deleted', flush=True)
        finally:
            console.close()


if __name__ == '__main__':
    main()
