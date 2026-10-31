"""Read installed native spawn economics without launching or modifying the game."""
import json
import sys
from pathlib import Path

root = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(root / '.build/python'))
import UnityPy
from UnityPy.classes import PPtr

bundles = Path('X:/SteamLibrary/steamapps/common/Risk of Rain 2/Risk of Rain 2_Data/StreamingAssets/aa/StandaloneWindows64')
rows = []
errors = []
def components(go):
    for reference in go.read_typetree()['m_Component']:
        component = PPtr(**reference['component'], assetsfile=go.assets_file).deref()
        if component.type.name == 'MonoBehaviour':
            yield component, component.read_typetree()

for path in sorted(bundles.glob('*_static_assets_all*.bundle')):
    # Spawn cards, masters and bodies often live in different serialized files.
    family = path.name.split('_static_assets_all')[0]
    env = UnityPy.load(*map(str, bundles.glob(family + '_*all*.bundle')))
    for obj in env.objects:
        if obj.type.name != 'MonoBehaviour':
            continue
        card = obj.read_typetree()
        if not card.get('m_Name', '').startswith('csc') or 'directorCreditCost' not in card:
            continue
        try:
            prefab = PPtr(**card['prefab'], assetsfile=obj.assets_file).deref()
            master, data = next((o, d) for o, d in components(prefab) if 'bodyPrefab' in d)
            body_object = PPtr(**data['bodyPrefab'], assetsfile=master.assets_file).deref()
            body_components = list(components(body_object))
            body = next(d for o, d in body_components if 'baseMaxHealth' in d)
        except Exception as error:
            errors.append(dict(card=card['m_Name'], error=str(error)))
            continue
        states = []
        for _, component in body_components:
            for field in ('initialStateType', 'mainStateType', 'deathState'):
                state = component.get(field, {})
                if isinstance(state, dict) and state.get('_typeName'):
                    states.append(state['_typeName'])
        rows.append(dict(card=card['m_Name'], body=body_object.read_typetree()['m_Name'], theme='-'.join(family.split('-')[:2]),
                         cost=card['directorCreditCost'], health=body['baseMaxHealth'], damage=body.get('baseDamage', 0),
                         champion=bool(body['isChampion']), token=body['baseNameToken'],
                         forbidden=bool(card.get('forbiddenAsBoss')), flags=card.get('requiredFlags', 0),
                         noElites=bool(card.get('noElites')), states=states,
                         deathRewards=next((d for _, d in body_components if 'bossDropTable' in d), None)))
out = root / '.build/arena-019-review/native-economy-assets.json'
out.write_text(json.dumps(rows, indent=2), encoding='utf-8')
(out.parent / 'native-economy-errors.json').write_text(json.dumps(errors, indent=2), encoding='utf-8')
print(f'Read {len(rows)} native cards from static bundles: {out}')
# Keep the portable test fixture tied to the extracted game, not guessed stats.
tokens = {}
for path in (bundles.parent.parent / 'Language/en').glob('*.json'):
    try:
        tokens.update(json.loads(path.read_text(encoding='utf-8-sig')).get('strings', {}))
    except ValueError:
        continue
if not tokens:
    raise RuntimeError('No installed English display-name tokens were read.')
fields = ('card', 'body', 'theme', 'cost', 'health', 'damage', 'champion', 'forbidden', 'flags', 'noElites', 'states')
fixture = [dict(**{key: row[key] for key in fields}, hasRewards=row['deathRewards'] is not None,
                released=row['token'] in tokens) for row in rows if 'junk' not in row['theme']]
(root / 'scripts/checks/native-economy.json').write_text(json.dumps(fixture, indent=2), encoding='utf-8')
