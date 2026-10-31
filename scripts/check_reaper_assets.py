"""Verify the two native Harvester variants without loading Unity or editing the game."""
from pathlib import Path
import json
import sys

root = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(root / '.build/python'))
import UnityPy

bundles = Path('X:/SteamLibrary/steamapps/common/Risk of Rain 2/Risk of Rain 2_Data/StreamingAssets/aa/StandaloneWindows64')
cards = {}
bodies = {}
for variant in ('reaper', 'reaperboss'):
    for kind in ('assets', 'static_assets'):
        source = next(bundles.glob(f'ror2-dlc4-{variant}_{kind}_all*'))
        env = UnityPy.load(str(source))
        for obj in env.objects:
            if obj.type.name != 'MonoBehaviour':
                continue
            data = obj.read_typetree()
            name = data.get('m_Name', '')
            if name in ('cscReaper', 'cscReaperBoss'):
                cards[name] = {key: data[key] for key in ('directorCreditCost', 'forbiddenAsBoss', 'nodeGraphType')}
            if not data.get('m_GameObject', {}).get('m_PathID'):
                continue
            name = obj.read().m_GameObject.read().m_Name
            if name not in ('ReaperBody', 'ReaperBossBody'):
                continue
            entry = bodies.setdefault(name, {'states': []})
            if 'baseNameToken' in data:
                entry['token'] = data['baseNameToken']
                entry['champion'] = data['isChampion']
            for field in ('initialStateType', 'mainStateType'):
                if field in data:
                    entry['states'].append(data[field]['_typeName'])

assert set(cards) == {'cscReaper', 'cscReaperBoss'}
assert all(not card['forbiddenAsBoss'] for card in cards.values()), 'Native forbiddenAsBoss alone no longer explains the invalid selection.'
assert bodies['ReaperBody']['token'] == bodies['ReaperBossBody']['token'] == 'REAPER_BODY_NAME'
assert all(body['champion'] for body in bodies.values())
assert 'EntityStates.Reaper.SpawnState' in bodies['ReaperBody']['states']
assert not any(state.startswith('EntityStates.ChampRushReaper.') for state in bodies['ReaperBody']['states'])
assert 'EntityStates.ChampRushReaper.SpawnState' in bodies['ReaperBossBody']['states']
output = {'cards': cards, 'bodies': bodies, 'gameLaunched': False}
(root / '.build/reaper-asset-audit.json').write_text(json.dumps(output, indent=2), encoding='utf-8')
print('PASS: installed native Harvester cards share a display token and champion eligibility, but have separate roaming/mission state machines. No Unity runtime loaded.')
