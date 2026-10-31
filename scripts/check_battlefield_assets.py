"""Inspect actual installed scenery, not just navigation points, for the flat arena."""
from pathlib import Path
import json,sys,math,re,itertools
root=Path(__file__).resolve().parent.parent
sys.path.insert(0,str(root/'.build/python'))
import numpy as np
import UnityPy
bundles=Path('X:/SteamLibrary/steamapps/common/Risk of Rain 2/Risk of Rain 2_Data/StreamingAssets/aa/StandaloneWindows64')
source=(root/'src/FortressBattlefield.cs').read_text(encoding='utf-8')
origin=tuple(float(v.strip().rstrip('f')) for v in re.search(r'Origin=new Vector3\(([^)]+)',source)[1].split(','))
scene_files=[next(bundles.glob('ror2-base-golemplains_'+kind+'_all*')) for kind in ('scenes','assets','static_assets')]
env=UnityPy.load(*(str(p) for p in scene_files))
scene_names={o.assets_file.name for o in env.objects if o.type.name=='RenderSettings'}
transforms={};gameobjects={};objects={}
for obj in env.objects:
    key=(obj.assets_file.name,obj.path_id);objects[key]=obj
    if obj.type.name=='Transform':transforms[key]=obj.read_typetree()
    elif obj.type.name=='GameObject':gameobjects[key]=obj.read_typetree()
cache={}
def matrix(key):
    if key in cache:return cache[key]
    d=transforms[key];x,y,z,w=(d['m_LocalRotation'][a] for a in ('x','y','z','w'))
    rotation=np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],[2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],[2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]])
    m=np.eye(4);m[:3,:3]=rotation@np.diag([d['m_LocalScale'][a] for a in ('x','y','z')]);m[:3,3]=[d['m_LocalPosition'][a] for a in ('x','y','z')]
    if d['m_Father']['m_PathID']:m=matrix((key[0],d['m_Father']['m_PathID']))@m
    cache[key]=m;return m
def active(key):
    d=transforms[key];go=gameobjects[(key[0],d['m_GameObject']['m_PathID'])]
    return go['m_IsActive'] and (not d['m_Father']['m_PathID'] or active((key[0],d['m_Father']['m_PathID'])))
def path(key):
    d=transforms[key];go=gameobjects[(key[0],d['m_GameObject']['m_PathID'])]
    return (path((key[0],d['m_Father']['m_PathID']))+'/' if d['m_Father']['m_PathID'] else '')+go['m_Name']
def has_networked_parent(key):
    d=transforms[key];go=gameobjects[(key[0],d['m_GameObject']['m_PathID'])]
    for part in go['m_Component']:
        ptr=part['component'];obj=objects.get((key[0],ptr['m_PathID']))
        if obj is None or obj.type.name!='MonoBehaviour':continue
        data=obj.read_typetree()
        if any(field in data for field in ('m_LocalPlayerAuthority','m_ServerOnly','m_AssetId','m_SceneId')):return True
    return bool(d['m_Father']['m_PathID']) and has_networked_parent((key[0],d['m_Father']['m_PathID']))
scenery=[]
for key,d in transforms.items():
    if key[0] not in scene_names or not active(key):continue
    go=gameobjects[(key[0],d['m_GameObject']['m_PathID'])]
    if go['m_Layer']!=11:continue
    for component in go['m_Component']:
        ptr=component['component'];obj=objects.get((key[0],ptr['m_PathID']))
        if obj is None or obj.type.name!='MeshCollider':continue
        c=obj.read()
        if not c.m_Enabled or c.m_IsTrigger:continue
        try:md=c.m_Mesh.read().object_reader.read_typetree()
        except Exception:continue
        aabb=md['m_LocalAABB'];mid=aabb['m_Center'];ext=aabb['m_Extent']
        corners=np.array([[mid[a]+sign[j]*ext[a] for j,a in enumerate(('x','y','z'))]+[1] for sign in itertools.product((-1,1),repeat=3)])
        world=(matrix(key)@corners.T).T[:,:3];lo=world.min(0);hi=world.max(0)
        play_lo=np.array(origin)+np.array([-183,-4,-183]);play_hi=np.array(origin)+np.array([183,200,183])
        if np.all(hi>=play_lo) and np.all(lo<=play_hi):
            assert not has_networked_parent(key),'A required obstacle would be excluded by the native-scenery selector: '+path(key)
            scenery.append({'path':path(key),'boundsMin':lo.tolist(),'boundsMax':hi.tolist()})
assert len(scenery)>10,'The new arena needs an actual scenery-clearance survey.'
assert any('Ring' in item['path'] for item in scenery),'The clearance volume must include the native rings, not miss them as the old navigation-only test did.'
assert any('Terrain' in item['path'] for item in scenery),'Native raised terrain must be replaced alongside props.'

# Inspect camera masks from the actual installed game, guarding the 0.7 regression.
global_env=UnityPy.load(str(bundles.parents[2]/'globalgamemanagers'))
layers=next(o.read_typetree()['layers'] for o in global_env.objects if o.type.name=='TagManager')
camera_env=UnityPy.load(str(next(bundles.glob('ror2-base-core-camera_assets_all*'))))
masks=[o.read_typetree()['m_CullingMask']['m_Bits'] for o in camera_env.objects if o.type.name=='Camera' and o.read().m_GameObject.read().m_Name=='Scene Camera']
assert masks
for mask in masks:
    assert mask&(1<<layers.index('Default')) and mask&(1<<layers.index('World'))
    assert not mask&(1<<layers.index('CollideWithCharacterHullOnly'))
# The HUD camera draws only the UI layer, so nested HUD canvases (War Council) must use it.
ui_masks=[o.read_typetree()['m_CullingMask']['m_Bits'] for o in camera_env.objects if o.type.name=='Camera' and o.read().m_GameObject.read().m_Name=='UI Camera']
assert ui_masks and all(mask==1<<layers.index('UI') for mask in ui_masks),'Review GauntletHud council layer: native UI camera mask changed'
for kind in ('ground','air'):
    node_env=UnityPy.load(str(next(bundles.glob('ror2-base-golemplains_nodegraph_assets_all*'))))
    assert any('nodes' in (d:=o.read_typetree()) and kind in d.get('m_Name','').lower() for o in node_env.objects if o.type.name=='MonoBehaviour')
# Decorative renderers beyond the old 366m cleanup box must be cleared too.
decor=[];missed=[]
for key,d in transforms.items():
    if key[0] not in scene_names or not active(key) or has_networked_parent(key):continue
    go=gameobjects[(key[0],d['m_GameObject']['m_PathID'])]
    for component in go['m_Component']:
        o=objects.get((key[0],component['component']['m_PathID']))
        if o is None or o.type.name not in ('MeshRenderer','ParticleSystemRenderer','SkinnedMeshRenderer','LineRenderer','TrailRenderer'):continue
        label=path(key)
        if go['m_Layer']==11 or label.startswith(('HOLDER:','FOLIAGE:')):decor.append(label)
        else:missed.append(label)
assert len(decor)>100 and any('Ring' in label for label in decor)
assert 'FindObjectsOfType<Renderer>(true)' in source and 'bounds.Intersects(renderer.bounds)' not in source
assert not missed,'Uncleared native decorative renderers remain: '+str(missed[:5])
print('Decorative renderer coverage:',len(decor),'Remaining roots:',sorted(set(p.split('/')[0] for p in missed))[:14])
assert (root/'assets/colosseum.meshbin').is_file()
# Native grass assets stay in the game installation; never copy them into the mod.
global_assets=UnityPy.load(str(next(bundles.glob('ror2-globalcontent_assets_all*'))))
textures={o.read().m_Name for o in global_assets.objects if o.type.name=='Texture2D'}
assert {'texGPGrassTerrain','texGPGrassTerrainNormal'}<=textures
material_source=(root/'src/ArenaMaterials.cs').read_text(encoding='utf-8')
assert 'USE_VERTEX_COLORS' in material_source and 'texGPGrassTerrain_png' in material_source
assert 'groundTexture' not in source,'Never destroy a game-owned grass texture.'
portal_assets=UnityPy.load(*(str(p) for p in bundles.glob('ror2-base-portalshop_*assets_all*')))
portal_components=[]
for o in portal_assets.objects:
    if o.type.name!='MonoBehaviour':continue
    data=o.read_typetree()
    if 'destinationScene' not in data and 'm_AssetId' not in data:continue
    component=o.read()
    if component.m_GameObject and component.m_GameObject.read().m_Name=='PortalShop':portal_components.append(data)
assert any('destinationScene' in c and 'useRunNextStageScene' in c for c in portal_components)
assert any('m_AssetId' in c and not c.get('m_ServerOnly') for c in portal_components),'Native blue portal must replicate to clients.'


receipt={'scene':'golemplains','combatDiameterMetres':358,'colosseumDiameterMetres':484,'decorativeRenderersCleared':len(decor),'nativeGrassVerified':True,'origin':origin,'nativeSolidSceneryToClear':len(scenery),'ringsIncluded':True,'raisedTerrainIncluded':True,'nativeCameraMasksVerified':True,'scenery':scenery,'gameLaunched':False}
(root/'.build/battlefield-asset-check.json').write_text(json.dumps(receipt,indent=2),encoding='utf-8')
print(f'PASS: actual Titanic Plains scene/collision survey covers {len(scenery)} solid scenery objects, including rings and raised terrain; native ground/air graphs and camera visibility verified. No game launched.')
