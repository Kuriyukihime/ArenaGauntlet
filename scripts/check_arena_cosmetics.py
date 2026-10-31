"""Validate cosmetic art, placement, preview freshness and default preservation."""
from pathlib import Path
import hashlib
import json
import math
import re

import numpy as np
from PIL import Image
from preview_colosseum import read_asset

ROOT=Path(__file__).resolve().parents[1]
ASSETS=ROOT/'assets';WORK=ROOT/'.build/arena-cosmetics'


def digest(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def validate_runtime_catalog():
    """Validate the case-sensitive game schema and all embedded art names."""
    catalog=json.loads((ASSETS/'arena_cosmetics.json').read_text(encoding='utf-8'))
    skins=catalog['Skins'];decor=catalog['Decor'];all_entries=skins+decor
    ids=[item['Id'] for item in all_entries]
    assert len(ids)==len(set(ids)) and all(re.fullmatch('[a-z0-9]{1,24}',item) for item in ids)
    assert all(item['Name'] and isinstance(item['Surfaces'],list) for item in all_entries)
    default_surfaces,_=read_asset(ASSETS/'colosseum.meshbin')
    decor_surfaces,parts=read_asset(ASSETS/'arena_decor.meshbin')
    surface_names={name for name,_ in default_surfaces+decor_surfaces}
    for theme in catalog['themes']:
        floor_surfaces,_=read_asset(ASSETS/theme['floor']['file'])
        surface_names.update(name for name,_ in floor_surfaces)
    used_ids=set()
    for part in parts:
        prefix,role=part['name'].rsplit('-',1)
        assert prefix.startswith('Decor') and prefix[5:] in {item['Id'] for item in decor},part['name']
        assert role==decor_surfaces[part['material']][0]
        used_ids.add(prefix[5:])
    assert used_ids=={item['Id'] for item in decor},'Every decor catalog entry must have geometry'
    for skin in skins:
        assert list(ASSETS.glob(f"arena_{skin['Id']}_*.png")),skin['Id']
        seen=set()
        for entry in skin['Surfaces']:
            assert entry['Name'] in surface_names and entry['Name'] not in seen
            seen.add(entry['Name'])
            assert all(0<=entry[key]<=1 for key in ('R','G','B','Smoothness'))
    for path in ASSETS.glob('arena_*.png'):
        role=path.stem.removeprefix('arena_').removesuffix('_normal')
        skin=next((item['Id'] for item in skins if role.startswith(item['Id']+'_')),None)
        role=role.removeprefix(skin+'_') if skin else role
        assert role in surface_names and role!='grass',f'Uncataloged PNG: {path.name}'
        if not path.stem.endswith('_normal'):
            normal=ASSETS/(path.stem+'_normal.png')
            fallback=ASSETS/f'arena_{role}_normal.png'
            assert normal.exists() or fallback.exists(),f'Missing normal fallback: {path.name}'
        if skin and role in ('limestone','marble','paving','basalt','roofstone'):
            with Image.open(path) as image:assert image.size==(1024,1024)
    return dict(skins=len(skins),decorationSets=len(decor),parts=len(parts),pngs=len(list(ASSETS.glob('arena_*.png'))))


def main():
    catalog=json.loads((ASSETS/'arena_cosmetics.json').read_text(encoding='utf-8'))
    assert catalog['schema']==1 and catalog['originalArt'] and not catalog['integratedInGame']
    assert digest(ASSETS/'colosseum.meshbin')==catalog['defaultArenaSha256']
    for name,expected in catalog['defaultAssets'].items():assert digest(ASSETS/name)==expected,name
    protected=json.loads((WORK/'baseline.json').read_text(encoding='utf-8-sig'))
    # Claude is building the integration concurrently. Preserve and report his
    # changes rather than treating a foreign code edit as an art regression.
    concurrent=[]
    for item in protected:
        path=Path(item['path'])
        if not path.is_file() or digest(path).upper()!=item['sha256']:
            concurrent.append(str(path.relative_to(ROOT)).replace('\\','/'))
    assert not any(name.startswith('assets/') for name in concurrent),'Original assets changed'
    runtime_contract=validate_runtime_catalog()
    public_zip=ROOT/'dist/ArenaGauntlet-0.39.0.zip'
    if public_zip.exists():assert digest(public_zip).upper()=='F821CDD442BAC22F2EC5D53576950E40E9718CDC71B97B410B611D3AA3531FAF'

    assets=[catalog['decorations']]+[theme['floor'] for theme in catalog['themes']]
    groups={}
    for record in assets:
        path=ASSETS/record['file'];assert digest(path)==record['sha256']
        surfaces,parts=read_asset(path)
        assert len(parts)==len(record['parts'])
        count=0
        for part in parts:
            name=part['name'];data=part['v'];faces=part['t'].reshape(-1,3);positions=data[:,:3]
            assert name.startswith('Decor') and np.isfinite(data).all(),name
            assert faces.min()>=0 and faces.max()<len(data)
            assert np.all(np.abs(np.linalg.norm(data[:,3:6],axis=1)-1)<.002),f'Non-unit normals: {name}'
            cross=np.cross(positions[faces[:,1]]-positions[faces[:,0]],positions[faces[:,2]]-positions[faces[:,0]])
            assert np.all(np.linalg.norm(cross,axis=1)>1e-8),f'Degenerate triangle: {name}'
            normal=data[faces,3:6].mean(axis=1)
            assert np.all((cross*normal).sum(axis=1)>-1e-6),f'Inconsistent winding: {name}'
            count+=len(faces)
            if name.startswith('DecorFloor'):
                assert np.linalg.norm(positions[:,[0,2]],axis=1).max()<178.9
                assert positions[:,1].min()>=.0399 and positions[:,1].max()<=.065
                assert np.all(data[:,4]>.999),f'Floor must face up: {name}'
            else:
                radius=np.linalg.norm(positions[:,[0,2]],axis=1)
                assert radius.min()>178,f'Decor enters fighting floor: {name}'
                assert np.linalg.norm(positions[:,[0,2]]-np.array((0,-171)),axis=1).min()>8,f'Ready brazier obstructed: {name}'
                if not name.startswith('Decortrophies'):
                    shop=(np.abs(positions[:,0])<54)&(positions[:,2]>-195)&(positions[:,2]<-172)&(positions[:,1]<20)
                    assert not shop.any(),f'Decor occupies merchant arcade: {name}'
                    assert np.min(np.minimum(np.abs(positions[:,0]),np.abs(positions[:,2])))>33,f'Gateway corridor obstructed: {name}'
                label=name.rsplit('-',1)[0]
                groups[label]=groups.get(label,0)+len(faces)
        assert count==record['triangles']

    all_bindings=[theme['materials'] for theme in catalog['themes']]+[catalog['defaultDecorationMaterials']]
    normals=set();albedos=set()
    for bindings in all_bindings:
        for role,material in bindings.items():
            assert len(material['tint'])==3 and all(0<=x<=1 for x in material['tint'])
            assert 0<=material['smoothness']<=1 and 0<=material['normalStrength']<=1
            for normal in (False,True):
                name=material['normal' if normal else 'albedo'];path=ASSETS/name
                with Image.open(path) as image:
                    image.load()
                    if normal:
                        pixels=np.asarray(image.convert('RGBA'))
                        assert np.all(pixels[:,:,0]==255) and np.all(pixels[:,:,2]==255),name
                        xy=pixels[:,:,[3,1]].astype(float)/255*2-1
                        assert np.max(np.sum(xy*xy,axis=2))<1,name
                        normals.add(name)
                    else:
                        assert image.width>=128 and image.height>=128
                        assert np.asarray(image.convert('RGB')).std()>1,name
                        albedos.add(name)
    preview_dir=ROOT/'dist/arena-cosmetics-review'
    preview=json.loads((preview_dir/'preview-info.json').read_text(encoding='utf-8'))
    assert not preview['gameLaunched'] and preview['exactAuthoredGeometry']
    assert preview['catalogSha256']==digest(ASSETS/'arena_cosmetics.json'),'Regenerate previews after art changes'
    assert preview['defaultArenaSha256']==catalog['defaultArenaSha256']
    for name,expected in preview['artInputHashes'].items():assert digest(ASSETS/name)==expected,f'Stale preview: {name}'
    assert len(preview['renders'])>=35 and all((preview_dir/name).is_file() for name in preview['renders'])
    assert (preview_dir/'index.html').is_file()
    report=dict(passed=True,artOnly=True,defaultArenaUnchanged=True,runtimeCatalog=runtime_contract,
        concurrentSourceChangesSinceInitialSnapshot=concurrent,
        originalGeometry=True,gameLaunched=False,triangleBudgetUserWaived=True,
        decorationTriangles=catalog['decorations']['triangles'],decorationTrianglesBySet=groups,
        floorTriangles={theme['id']:theme['floor']['triangles'] for theme in catalog['themes']},
        uniqueAlbedoBytes={theme['id']:theme['uniqueAlbedoBytes'] for theme in catalog['themes']},
        reusedNormalFiles=sorted(normals),albedoFiles=sorted(albedos),previewCount=len(preview['renders']),
        newFiles=[str(path.relative_to(ROOT)).replace('\\','/') for path in ASSETS.glob('arena_*') if str(path) not in {item['path'] for item in protected}])
    (WORK/'art-check.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(report,indent=2))


if __name__=='__main__':main()
