"""Original deterministic arena skins, matching floors and optional decorations.

Art assets only. Never rewrites the default colosseum, C# or release metadata.
Claude integrates the catalog's material bindings and group toggles.
"""
from collections import OrderedDict
from pathlib import Path
import hashlib
import json
import math
import struct
import shutil

import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from build_castle_models import PARTS, SURFACES, string

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'assets'
WORK = ROOT / '.build/arena-cosmetics'
SEED = 20261030
STONE = ('limestone', 'marble', 'paving', 'basalt', 'roofstone')
GRASS_TINTS = {'default':[.90,.96,.86], 'gilded':[.63,.65,.45],
               'lunar':[.66,.77,.84], 'ruins':[.74,.88,.60]}
THEMES = OrderedDict([
    ('gilded', dict(name='Gilded Basalt', seed=31,
        description='Charcoal basalt, champagne stone and gilded bronze; a laurel-star court.',
        colors=[(51,54,61), (193,177,133), (47,48,52), (31,35,40), (79,72,61)],
        smoothness=[.19,.34,.19,.23,.17], accent=(205,158,69), cloth=(91,24,36),
        floor='Laurel of Victory')),
    ('lunar', dict(name='Moonlit Sanctuary', seed=67,
        description='Cool marble, midnight stone and silver; a crescent and constellation court.',
        colors=[(137,155,178), (220,229,235), (54,68,94), (35,45,67), (68,88,119)],
        smoothness=[.18,.33,.22,.20,.17], accent=(185,205,223), cloth=(24,47,85),
        floor='Court of Selene')),
    ('ruins', dict(name='Overgrown Ruins', seed=101,
        description='Sun-worn limestone, bronze patina and moss; a fragmented old victory mosaic.',
        colors=[(173,158,130), (204,191,160), (132,127,104), (78,85,66), (138,121,96)],
        smoothness=[.08,.13,.07,.08,.08], accent=(141,127,73), cloth=(65,83,47),
        floor='Forgotten Laurels')),
])


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def save_albedo(image,path):
    # Indexed PNG keeps full spatial resolution. Use it only when the decoded
    # palette is visually equivalent within a tight RGB error bound.
    palette=image.quantize(colors=256,method=Image.Quantize.MEDIANCUT,dither=Image.Dither.NONE)
    error=np.abs(np.asarray(image.convert('RGB'),float)-np.asarray(palette.convert('RGB'),float))
    (palette if error.mean()<.75 and error.max()<10 else image).save(path,optimize=True)


def noise(rng, size, cells):
    source = np.uint8(rng.uniform(0, 255, (cells, cells)))
    return np.asarray(Image.fromarray(source).resize((size, size), Image.Resampling.BICUBIC), float) / 255


def laurel(draw, center, radius, color, width=5):
    cx, cy = center
    for side in (-1, 1):
        points = []
        for angle in np.linspace(-75, 82, 32):
            a = math.radians(angle)
            points.append((cx + side * radius * math.cos(a), cy - radius * math.sin(a)))
        draw.line(points, fill=color, width=width)
        for angle in np.linspace(-65, 72, 13):
            a = math.radians(angle)
            x, y = cx + side * radius * math.cos(a), cy - radius * math.sin(a)
            tangent = np.array((side * math.sin(a), math.cos(a)))
            direction = np.array((side * .8, -.6)) + tangent * .35
            direction /= np.linalg.norm(direction)
            normal = np.array((-direction[1], direction[0]))
            base = np.array((x, y))
            length = radius * .28
            vertices = [base, base + direction * length * .45 + normal * length * .24,
                        base + direction * length, base + direction * length * .45 - normal * length * .24]
            draw.polygon([tuple(point) for point in vertices], fill=color)


def cloth_texture(theme_id, palette):
    width, height = 512, 1024
    rng = np.random.default_rng(SEED + palette['seed'] + 400)
    yy, xx = np.mgrid[:height, :width]
    weave = np.sin(xx * math.pi) * .6 + np.sin(yy * math.pi * .5) * 1.4
    tone = rng.normal(0, .8, (height, width)) + weave
    base = np.array(palette['cloth'])[None, None, :]
    pixels = base + tone[:, :, None]
    if theme_id == 'ruins':
        stain = np.asarray(Image.fromarray(np.uint8(rng.uniform(0, 255, (24, 12)))).resize((width, height), Image.Resampling.BICUBIC)) / 255
        pixels *= (.68 + stain[:, :, None] * .32)
    image = Image.fromarray(np.uint8(np.clip(pixels, 0, 255)))
    draw = ImageDraw.Draw(image)
    accent = tuple(palette['accent'])
    for inset in (22, 35):
        draw.rectangle((inset, 28, width-inset-1, height-75), outline=accent, width=3)
    for y in range(60, 925, 44):
        for x in (26, width-38):
            draw.line([(x,y),(x+11,y),(x+11,y+22),(x+5,y+22),(x+5,y+11)], fill=accent, width=3)
    if theme_id == 'lunar':
        draw.ellipse((140,260,375,530), fill=accent)
        draw.ellipse((213,237,410,490), fill=tuple(palette['cloth']))
        for cx, cy, size in ((154,568,13),(330,598,10),(235,645,16)):
            draw.polygon([(cx,cy-size),(cx+size*.32,cy),(cx,cy+size),(cx-size*.32,cy)],fill=accent)
    else:
        laurel(draw, (256,485), 127, accent, 5)
        draw.line((256,385,256,593), fill=accent, width=8)
        draw.polygon([(256,345),(238,395),(274,395)], fill=accent)
        if theme_id == 'gilded':
            draw.line((195,590,314,390), fill=accent, width=7)
            draw.line((314,590,195,390), fill=accent, width=7)
    path = ASSETS / f'arena_{theme_id}_cloth.png'
    save_albedo(image,path)
    return path


def packed_normal(height, strength=1):
    gy, gx = np.gradient(height)
    vector = np.dstack((-gx*strength, -gy*strength, np.ones_like(gx)))
    vector /= np.linalg.norm(vector, axis=2, keepdims=True)
    result = np.full((*height.shape, 4), 255, np.uint8)
    result[:, :, 1] = np.uint8((vector[:, :, 1]*.5+.5)*255)
    result[:, :, 3] = np.uint8((vector[:, :, 0]*.5+.5)*255)
    return result


def textures():
    bindings = {}
    for theme_id, theme in THEMES.items():
        rng = np.random.default_rng(SEED + theme['seed'])
        bindings[theme_id] = {}
        for index, surface in enumerate(STONE):
            original = np.asarray(Image.open(ASSETS / f'arena_{surface}.png').convert('RGB'), float)
            luminance = original.mean(axis=2)
            # Keep all original masonry joints/relief in register with the
            # shared normal map. Additional variation is pigmentation only.
            relief = np.clip(luminance / np.median(luminance), .35, 1.35)
            n = len(relief)
            field = noise(rng, n, 18)
            fine = noise(rng, n, 64)
            color = np.array(theme['colors'][index])[None, None, :] * relief[:, :, None]
            if theme_id == 'gilded':
                vein = np.exp(-((field-.5)/.018)**2) * .10
                if surface in ('marble','basalt'):
                    color = color*(1-vein[:, :, None]) + np.array(theme['accent'])*vein[:, :, None]
            elif theme_id == 'lunar':
                yy, xx = np.mgrid[:n, :n]
                vein = np.exp(-(np.sin(xx*.007+yy*.003+field*.6)/.032)**2)*.09
                color += vein[:, :, None]*np.array((14,20,29))
            else:
                moss = np.clip((field*.7+fine*.3-.53)*2.1,0,.46)
                if surface == 'marble':moss *= .55
                color = color*(1-moss[:, :, None]) + np.array((48,65,38))*moss[:, :, None]
                color *= (.93 + fine[:, :, None]*.09)
            path = ASSETS / f'arena_{theme_id}_{surface}.png'
            save_albedo(Image.fromarray(np.uint8(np.clip(color,0,255))),path)
            bindings[theme_id][surface] = dict(albedo=path.name, normal=f'arena_{surface}_normal.png',
                tint=[1,1,1], smoothness=theme['smoothness'][index], glow=0,
                normalStrength=.45, sharedNormal=True)
        n = 512
        field = noise(rng,n,28)
        yy, xx = np.mgrid[:n,:n]
        brushed = np.sin(yy*.33+field*.45)*1.4 + (field-.5)*8
        color = np.array(theme['accent'])[None,None,:] + brushed[:,:,None]
        if theme_id == 'ruins':
            patina = np.clip((field-.55)*1.4,0,.35)
            color=color*(1-patina[:,:,None])+np.array((45,105,87))*patina[:,:,None]
        path=ASSETS/f'arena_{theme_id}_accent.png'
        save_albedo(Image.fromarray(np.uint8(np.clip(color,0,255))),path)
        bindings[theme_id]['accent']=dict(albedo=path.name,normal='arena_accent_normal.png',
            tint=[1,1,1],smoothness=.48 if theme_id!='ruins' else .23,glow=0,normalStrength=.45,sharedNormal=True)
        path=cloth_texture(theme_id,theme)
        bindings[theme_id]['cloth']=dict(albedo=path.name,normal='arena_cloth_normal.png',
            tint=[1,1,1],smoothness=.055,glow=0,normalStrength=.45,sharedNormal=True)
        bindings[theme_id]['ember']=dict(albedo='arena_ember.png',normal='arena_ember_normal.png',
            tint=[1,1,1],smoothness=.1,glow=0,normalStrength=.45,sharedNormal=True)
        bindings[theme_id]['moss']=dict(albedo='arena_moss.png',normal='arena_moss_normal.png',
            tint=[1,1,1],smoothness=.035,glow=0,normalStrength=.45,sharedNormal=True)

    rng=np.random.default_rng(SEED+901)
    n=512;yy,xx=np.mgrid[:n,:n]
    metal=(noise(rng,n,32)-.5)*.10+np.sin(yy*.33)*.025
    Image.fromarray(packed_normal(metal,.7)).save(ASSETS/'arena_accent_normal.png',optimize=True)
    n=1024;yy,xx=np.mgrid[:n,:n]
    fabric=(np.sin(xx*math.pi*.5)+np.sin(yy*math.pi*.5))*.07
    Image.fromarray(packed_normal(fabric,.7)).save(ASSETS/'arena_cloth_normal.png',optimize=True)
    field=noise(rng,512,24);fine=noise(rng,512,128)
    moss=np.array((46,66,36))[None,None,:]*(.65+field[:,:,None]*.6)+fine[:,:,None]*11
    save_albedo(Image.fromarray(np.uint8(moss)),ASSETS/'arena_moss.png')
    ember=np.zeros((128,128,3),np.uint8)
    ember[:]=np.array((183,59,12));ember=np.uint8(np.clip(ember.astype(float)*(noise(rng,128,12)[:,:,None]*.7+.65),0,255))
    Image.fromarray(ember).save(ASSETS/'arena_ember.png',optimize=True)
    Image.fromarray(packed_normal(noise(rng,128,12),.6)).save(ASSETS/'arena_ember_normal.png',optimize=True)
    # File-name fallback is the loader's normal-map reuse contract. The moss
    # role has its own small relief instead of duplicating a 1024px stone map.
    Image.fromarray(packed_normal(field*.15+fine*.06,.7)).save(ASSETS/'arena_moss_normal.png',optimize=True)
    # Independent decoration sets also have a complete default-material theme.
    default=dict(THEMES['gilded'],seed=901,accent=(171,128,66),cloth=(34,73,103))
    cloth_texture('default',default)
    gold=np.asarray(Image.open(ASSETS/'arena_gilded_accent.png').convert('RGB'),float)
    bronze=gold*np.array(default['accent'])/np.array(THEMES['gilded']['accent'])
    Image.fromarray(np.uint8(np.clip(bronze,0,255))).save(ASSETS/'arena_default_accent.png',optimize=True)
    bindings['default']={surface:dict(albedo=f'arena_{surface}.png',normal=f'arena_{surface}_normal.png',
        tint=([.85,.90,.88] if surface=='basalt' else [1,1,.98] if surface=='marble' else [1,.98,.94] if surface=='roofstone' else [1,.98,.93]),
        smoothness=dict(zip(STONE,[.12,.21,.09,.11,.13]))[surface],glow=0,normalStrength=.45,sharedNormal=True) for surface in STONE}
    for role in ('accent','cloth','ember','moss'):
        bindings['default'][role]=dict(bindings['gilded'][role])
    shutil.copyfile(ASSETS/'arena_default_accent.png',ASSETS/'arena_accent.png')
    shutil.copyfile(ASSETS/'arena_default_cloth.png',ASSETS/'arena_cloth.png')
    bindings['default']['accent']['albedo']='arena_accent.png'
    bindings['default']['cloth']['albedo']='arena_cloth.png'
    # Only obsolete files created by this script, never original arena files.
    for name in ('arena_default_accent.png','arena_default_cloth.png',
                 'arena_cosmetic_accent_normal.png','arena_cosmetic_cloth_normal.png',
                 'arena_cosmetic_ember.png','arena_ruins_moss.png'):
        (ASSETS/name).unlink(missing_ok=True)
    return bindings


def write_mesh(path, surfaces):
    # Match the existing CAST1 writer and full-attribute vertex welding.
    used={key[-1] for key in PARTS}
    surfaces=OrderedDict((name,value) for name,value in surfaces.items() if name in used)
    stats=[]
    pending=path.with_suffix('.meshbin.tmp')
    with pending.open('wb') as stream:
        stream.write(b'CAST1');stream.write(struct.pack('<i',len(surfaces)))
        for name,material in surfaces.items():
            color,smoothness,glow=material
            string(stream,name);stream.write(struct.pack('<5f',*color,smoothness,glow))
        stream.write(struct.pack('<i',len(PARTS)))
        for key,part in PARTS.items():
            _,label,_,_,_,material=key
            data=np.asarray(part['v'],dtype='<f4')
            unique,inverse=np.unique(data,axis=0,return_inverse=True)
            triangles=inverse[np.asarray(part['t'],np.int32)].astype('<i4')
            # Discard only zero-area triangles from closed tips.
            positions=unique[:,:3]
            area=np.linalg.norm(np.cross(positions[triangles[:,1]]-positions[triangles[:,0]],positions[triangles[:,2]]-positions[triangles[:,0]]),axis=1)
            triangles=triangles[area>1e-8]
            string(stream,label+'-'+material)
            stream.write(struct.pack('<iii?i',0,0,list(surfaces).index(material),False,0))
            stream.write(struct.pack('<ii',len(unique),triangles.size))
            stream.write(unique.tobytes());stream.write(triangles.tobytes())
            stats.append(dict(name=label+'-'+material,group=label,material=material,
                vertices=len(unique),triangles=len(triangles),bounds=[positions.min(0).tolist(),positions.max(0).tolist()],
                minimumRadius=float(np.linalg.norm(positions[:,[0,2]],axis=1).min())))
    pending.replace(path)
    return dict(file=path.name,bytes=path.stat().st_size,sha256=digest(path),parts=stats,
                triangles=sum(item['triangles'] for item in stats),vertices=sum(item['vertices'] for item in stats))


def main():
    from arena_cosmetic_geometry import build_decorations, build_floor
    WORK.mkdir(parents=True,exist_ok=True)
    code_paths=list((ROOT/'src').rglob('*.cs'))+list((ROOT/'package').rglob('*'))+[ROOT/'BazaarGauntlet.csproj']
    source_before={path:digest(path) for path in code_paths if path.is_file()}
    protected={path.name:digest(path) for path in ASSETS.iterdir() if path.name=='colosseum.meshbin' or path.name=='colosseum-info.json' or path.name in {f'arena_{s}{suffix}.png' for s in STONE for suffix in ('','_normal')}}
    bindings=textures()
    roles=OrderedDict((name,(tuple(value['tint']),value['smoothness'],value['glow']))
                      for name,value in bindings['default'].items())
    PARTS.clear();SURFACES.clear();SURFACES.update(roles)
    decorations=build_decorations()
    decor_report=write_mesh(ASSETS/'arena_decor.meshbin',roles)
    floor_reports={}
    for theme in THEMES:
        PARTS.clear()
        build_floor(theme)
        floor_reports[theme]=write_mesh(ASSETS/f'arena_{theme}_floor.meshbin',roles)
    themes=[]
    for theme_id,theme in THEMES.items():
        names={binding['albedo'] for binding in bindings[theme_id].values() if binding['albedo'].startswith('arena_'+theme_id+'_')}
        texture_bytes=sum((ASSETS/name).stat().st_size for name in names)
        themes.append(dict(id=theme_id,name=theme['name'],description=theme['description'],
            seed=SEED+theme['seed'],floorName=theme['floor'],materials=bindings[theme_id],
            floor=floor_reports[theme_id],uniqueAlbedoBytes=texture_bytes,
            grassTint=GRASS_TINTS[theme_id]))
    runtime_skins=[]
    for theme in themes:
        entries=[dict(Name=name,R=value['tint'][0],G=value['tint'][1],B=value['tint'][2],
                      Smoothness=value['smoothness']) for name,value in theme['materials'].items()]
        entries.append(dict(Name='grass',R=theme['grassTint'][0],G=theme['grassTint'][1],
                            B=theme['grassTint'][2],Smoothness=.025))
        runtime_skins.append(dict(Id=theme['id'],Name=theme['name'],Surfaces=entries))
    catalog=dict(schema=1,authoring='scripts/build_arena_cosmetics.py',originalArt=True,
        Skins=runtime_skins,Decor=[dict(Id=item['id'],Name=item['name'],Surfaces=[]) for item in decorations],
        artOnly=True,integratedInGame=False,defaultArenaSha256=protected['colosseum.meshbin'],
        defaultAssets=protected,themes=themes,defaultDecorationMaterials=bindings['default'],
        decorations=decor_report,decorationSets=decorations,
        integration=dict(geometryFormat='CAST1',normalPacking='AG: R=255, G=Y, B=255, A=X',
            skinsAndDecorMatchCurrentLoader=True,floorsNeedLoaderSupport=True,
            floorCollision=False,floorOverlayHeight=[.04,.064],floorRenderAfterBase=True,
            defaultArenaChangesRequired=False,combatRadius=178,
            readyBrazier=[0,0,-171],decorGroupPrefix='Decor',
            notes=['Load only the selected skin floor; all new floor meshes are visual-only.',
                   'Keep default colosseum and its collision/navmesh intact.',
                   'Skins/Decor use Claude\'s case-sensitive runtime catalog schema; extra fields are authoring metadata.',
                   'Five stone normals are reused through the loader\'s existing filename fallback.',
                   'Decoration sets are independent group-prefix toggles; no animation or text labels.',
                   'Brazier fire VFX are optional integration anchors, not included game code.']))
    (ASSETS/'arena_cosmetics.json').write_text(json.dumps(catalog,indent=2)+'\n',encoding='utf-8')
    assert all(digest(ASSETS/name)==value for name,value in protected.items()),'Default arena art changed'
    assert all(digest(path)==value for path,value in source_before.items()),'Source changed during art generation'
    print(json.dumps(dict(decorTriangles=decor_report['triangles'],decorBytes=decor_report['bytes'],
        themes=[dict(id=theme['id'],floorTriangles=theme['floor']['triangles'],albedoBytes=theme['uniqueAlbedoBytes']) for theme in themes]),indent=2),flush=True)


if __name__=='__main__':main()
