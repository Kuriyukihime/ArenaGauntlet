"""Verify the installed base-game merchant prefabs, without loading Unity."""
from pathlib import Path
import json,sys
ROOT=Path(__file__).resolve().parent.parent
sys.path.insert(0,str(ROOT/'.build/python'))
import UnityPy
BUNDLES=Path('X:/SteamLibrary/steamapps/common/Risk of Rain 2/Risk of Rain 2_Data/StreamingAssets/aa/StandaloneWindows64')
report=[]
jobs=[('ror2-base-drones_',{'Drone1Broken','Drone2Broken','MissileDroneBroken'}),
      ('ror2-base-lunarshopterminal_',{'LunarShopTerminal'}),('ror2-base-scrapper_',{'Scrapper'})]
for prefix,names in jobs:
 for path in BUNDLES.glob(prefix+'*assets_all*'):
  env=UnityPy.load(str(path))
  for obj in env.objects:
   if obj.type.name!='GameObject':continue
   data=obj.read_typetree()
   if data.get('m_Name') not in names:continue
   components=[]
   for reference in obj.read().m_Component:
    component=reference.component.read()
    c=component.object_reader.read_typetree()
    fields={key:c[key] for key in ('cost','costType','automaticallyScaleCostWithDifficulty','requiredExpansion','requiredUnlockable','m_AssetId','selfGeneratePickup','destroyAfterSummoning','masterPrefab','interactor','m_Radius','m_Center','m_Size') if key in c}
    if fields:components.append(fields)
   report.append(dict(bundle=path.name,name=data['m_Name'],components=components))
found={r['name'] for r in report}
assert {'Drone1Broken','Drone2Broken','MissileDroneBroken','LunarShopTerminal','Scrapper'}<=found,found
for entry in report:
 if entry['name'].endswith('Broken'):
  purchase=next(c for c in entry['components'] if 'costType' in c)
  assert purchase['costType']==1 and purchase['cost']>0,entry
  assert purchase.get('requiredExpansion',{}).get('m_PathID',0)==0,'Base drones must not require another expansion'
(ROOT/'.build/native-shop-assets.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('PASS: native lunar terminals, scrapper and three base-game drones; drone purchases need no extra expansion.')
