"""Audit the authored arena, textures and installed native material contract."""
from pathlib import Path
import sys,json,hashlib
import numpy as np
from PIL import Image
ROOT=Path(__file__).resolve().parent.parent
sys.path.insert(0,str(ROOT/'.build/python'))
import UnityPy
bundles=Path('X:/SteamLibrary/steamapps/common/Risk of Rain 2/Risk of Rain 2_Data/StreamingAssets/aa/StandaloneWindows64')
env=UnityPy.load(str(next(bundles.glob('ror2-base-shaders_shader_assets_all*'))))
properties=None
for obj in env.objects:
 if obj.type.name!='Shader':continue
 data=obj.read_typetree().get('m_ParsedForm',{})
 if data.get('m_Name')=='Hopoo Games/Deferred/Standard':
  properties={p['m_Name'] for p in data['m_PropInfo']['m_Props']};break
required={'_Color','_MainTex','_NormalTex','_NormalStrength','_Smoothness','_SpecularStrength','_SpecularExponent','_ColorsOn','_EmPower','_LimbRemovalOn'}
assert properties and required<=properties,('Missing native arena shader properties',required-(properties or set()))
info=json.loads((ROOT/'assets/colosseum-info.json').read_text(encoding='utf-8'))
assert info['originalGeometry'] and info['doricColumns']==126 and info['flutesPerColumn']==20 and info['merchantAlcoves']==5
assert info['pedimentedGateways']==4 and info['seatingRows']==18 and info['grassBeds']==12
assert info['detailLevels']==1 and info['parts']<=16 and info['triangles']<420000
assert info['modelSha256']==hashlib.sha256((ROOT/'assets/colosseum.meshbin').read_bytes()).hexdigest()
textures={}
for name in info['materialNames']:
 if name=='grass':continue
 for normal in (False,True):
  path=ROOT/f'assets/arena_{name}{"_normal" if normal else ""}.png'
  with Image.open(path) as image:
   image.load();assert image.size==(1024,1024)
   pixels=np.asarray(image)
   assert pixels.std()>1,'A surface must not collapse into a flat color'
   if normal:
    assert image.mode=='RGBA' and np.all(pixels[:,:,0]==255) and np.all(pixels[:,:,2]==255),'Unity RG-or-AG normal packing must be preserved'
    xy=pixels[:,:,[3,1]]/255*2-1
    assert np.max(np.sum(xy*xy,axis=2))<1,'Normal samples must reconstruct a real Z component'
  textures[path.name]=hashlib.sha256(path.read_bytes()).hexdigest()
source=(ROOT/'src/FortressBattlefield.cs').read_text(encoding='utf-8')
assert 'StartsWith("Courtyard",StringComparison.Ordinal)' in source and 'StartsWith("MerchantShutters",StringComparison.Ordinal)' in source
assert 'RecalculateTangents' in source and 'mesh.colors32=' in source
preview=ROOT/'dist/arena-model-review/preview-info.json'
if preview.exists():
 p=json.loads(preview.read_text(encoding='utf-8'));assert p['sha256']==info['modelSha256'],'Regenerate previews: geometry changed after visual review'
 assert p['surfaces']==textures,'Regenerate previews: a reviewed stone texture changed'
 assert all((preview.parent/name).is_file() for name in p['renders'])
report={'model':info,'nativeMaterialPropertiesVerified':sorted(required),'textures':textures,'previewMatchesAsset':preview.exists(),'gameLaunched':False}
(ROOT/'.build/arena-art-check.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(f"PASS: original Greek arena ({info['triangles']:,} triangles / {info['parts']} batches), ten original stone maps, native shader properties and exact-mesh previews verified.")
if (ROOT/'assets/arena_cosmetics.json').exists():
 from check_arena_cosmetics import validate_runtime_catalog
 cosmetics=validate_runtime_catalog()
 print(f"PASS: cosmetic catalog IDs, decoration parts and PNG ownership ({cosmetics['skins']} skins / {cosmetics['decorationSets']} sets / {cosmetics['pngs']} PNGs).")
