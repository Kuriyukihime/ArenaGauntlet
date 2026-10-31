"""GPU studio review of the exact cosmetic CAST1 meshes and authored textures.

No game is launched. Original arena assets and their existing previews stay put.
"""
from pathlib import Path
import argparse
import hashlib
import json
import math
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont
import preview_colosseum as base

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'.build/render-python'))
import moderngl

COMMON_JOBS=[
    ('overview','Whole arena',(500,530,590),(0,7,0),42,'ortho'),
    ('propylon','Doric gateway',(45,22,156),(0,15,222),48,'perspective'),
    ('courtyard','From the fighting floor',(91,5,78),(-8,20,216),65,'perspective'),
    ('columns','Stone and fluting detail',(34,11,191),(25,10,212.6),44,'perspective'),
    ('reverse','Reverse overview',(-500,460,-520),(0,8,0),42,'ortho'),
    ('merchants','Merchant arcade',(39,12,-139),(0,4,-182),60,'perspective'),
    ('floor','Matching arena floor',(64,72,79),(0,.04,0),65,'perspective'),
]
DETAIL_JOBS=[
    ('sentinels','Guardians of the Arena',(107,9,116),(128.62,8,128.62),42,'perspective'),
    ('standards','Champion Standards',(37,8,149),(47.08,8.8,175.70),42,'perspective'),
    ('braziers','Victory Braziers',(63,6,158),(69.61,5.1,168.05),38,'perspective'),
    ('trophies','Victors Gallery',(2.7,3.5,-183),(0,2.7,-189),42,'perspective'),
    ('laurels','Laurels of Renown',(55,31,210),(59.27,32,221.23),34,'perspective'),
]


class Studio:
    def __init__(self,catalog,width=1680,height=1120,ss=2):
        self.catalog=catalog;self.width=width;self.height=height;self.size=(width*ss,height*ss)
        self.ctx=ctx=moderngl.create_standalone_context(require=330)
        self.program=ctx.program(vertex_shader=base.VERT,fragment_shader=base.FRAG)
        self.shadow=ctx.program(vertex_shader=base.SHADOW_VERT,fragment_shader=base.SHADOW_FRAG)
        self.sun=base.unit((-.6,1,-.7))
        self.light=base.ortho(-350,350,-350,350,1,1600)@base.look(self.sun*700,(0,0,0))
        self.depth=ctx.depth_texture((4096,4096));self.depth.compare_func='<='
        self.depth.repeat_x=self.depth.repeat_y=False
        self.sfbo=ctx.framebuffer(depth_attachment=self.depth)
        self.color=ctx.texture(self.size,4);self.z=ctx.depth_renderbuffer(self.size)
        self.frame=ctx.framebuffer([self.color],self.z)
        self.texture_cache={};self.geometry_cache={}
        self.program['light_vp'].write(self.light.T.astype('f4').tobytes())
        self.program['sun'].value=tuple(self.sun)
        self.program['albedo'].value=0;self.program['bump'].value=1;self.program['shadowmap'].value=2
        self.default_surfaces,self.default_parts=base.read_asset(ROOT/'assets/colosseum.meshbin')
        for part in self.default_parts:
            if part['name'].startswith('MerchantShutters'):part['v'][:,1]-=7.5

    def geometry(self,key,surfaces,parts):
        if key in self.geometry_cache:return self.geometry_cache[key]
        result=[]
        for part in parts:
            vbo=self.ctx.buffer(part['v'].astype('f4').tobytes())
            ibo=self.ctx.buffer(part['t'].astype('i4').tobytes())
            vao=self.ctx.vertex_array(self.program,[(vbo,'3f 3f 2f','in_pos','in_normal','in_uv')],ibo)
            shadow=self.ctx.vertex_array(self.shadow,[(vbo,'3f 20x','in_pos')],ibo)
            role,parameters=surfaces[part['material']]
            result.append((part['name'],role,parameters,vao,shadow))
        self.geometry_cache[key]=result
        return result

    def texture(self,path,normal=False):
        key=(str(path),normal)
        if key in self.texture_cache:return self.texture_cache[key]
        if path is None:image=Image.new('RGBA',(4,4),(255,128,255,128))
        else:image=Image.open(path).convert('RGBA' if normal else 'RGB')
        image=image.transpose(Image.Transpose.FLIP_TOP_BOTTOM)
        texture=self.ctx.texture(image.size,4 if normal else 3,image.tobytes())
        texture.build_mipmaps();texture.filter=(moderngl.LINEAR_MIPMAP_LINEAR,moderngl.LINEAR)
        texture.anisotropy=8
        self.texture_cache[key]=texture
        return texture

    def scene(self,theme_id,with_decor=True):
        geometry=self.geometry('default',self.default_surfaces,self.default_parts)
        if theme_id!='default':
            surfaces,parts=base.read_asset(ROOT/f'assets/arena_{theme_id}_floor.meshbin')
            geometry=geometry+self.geometry(theme_id,surfaces,parts)
        if with_decor:
            surfaces,parts=base.read_asset(ROOT/'assets/arena_decor.meshbin')
            geometry=geometry+self.geometry('decor',surfaces,parts)
        bindings=self.catalog['defaultDecorationMaterials'] if theme_id=='default' else next(t['materials'] for t in self.catalog['themes'] if t['id']==theme_id)
        ctx=self.ctx;ctx.enable(moderngl.DEPTH_TEST);ctx.disable(moderngl.CULL_FACE)
        self.sfbo.use();self.sfbo.clear(depth=1)
        self.shadow['vp'].write(self.light.T.astype('f4').tobytes())
        for name,role,parameters,vao,shadow in geometry:
            if not name.startswith(('Courtyard','DecorFloor')):shadow.render()
        return geometry,bindings

    def render(self,scene,job,title):
        key,label,eye,target,fov,kind=job
        geometry,bindings=scene
        width,height=self.width,self.height
        projection=base.ortho(-255*width/height,255*width/height,-255,255,1,2000) if kind=='ortho' else base.perspective(fov,width/height,.2,1200)
        self.frame.use();self.frame.clear(.15,.20,.23,1,depth=1)
        self.program['vp'].write((projection@base.look(eye,target)).T.astype('f4').tobytes())
        self.program['eye'].value=eye;self.depth.use(2)
        for name,role,parameters,vao,shadow in geometry:
            if role=='grass':
                albedo=ROOT/'.build/native-grass-reference.png';normal=None;strength=0
                skin=next((theme for theme in self.catalog['themes'] if theme['materials'] is bindings),None)
                tint=skin['grassTint'] if skin else parameters[:3]
            else:
                binding=bindings[role]
                albedo=ROOT/'assets'/binding['albedo'];normal=ROOT/'assets'/binding['normal']
                tint=binding['tint'];strength=binding['normalStrength']
            self.texture(albedo).use(0);self.texture(normal,True).use(1)
            self.program['tint'].value=tuple(tint);self.program['strength'].value=strength
            vao.render()
        image=Image.frombytes('RGBA',self.size,self.frame.read(components=4)).transpose(Image.Transpose.FLIP_TOP_BOTTOM).convert('RGB')
        image=image.resize((width,height),Image.Resampling.LANCZOS)
        draw=ImageDraw.Draw(image);font=lambda size,bold=False:ImageFont.truetype('C:/Windows/Fonts/'+('segoeuib.ttf' if bold else 'segoeui.ttf'),size)
        draw.rectangle((0,0,width,91),fill='#101922')
        draw.text((32,15),title+' / '+label,font=font(29,True),fill='#f1e9dc')
        draw.text((33,55),'Exact authored assets | studio lighting | cosmetics not yet integrated in game',font=font(17),fill='#adbdc7')
        return image


def gallery(output,catalog):
    data={theme['id']:theme['name'] for theme in catalog['themes']}
    data={'default':'Original arena',**data}
    options=''.join(f'<option value="{key}">{name}</option>' for key,name in data.items())
    angles=''.join(f'<option value="{key}">{label}</option>' for key,label,*_ in COMMON_JOBS)
    cards=''.join(f'<a href="{key}-{view}.png"><img src="{key}-{view}.png"><span>{name} · {label}</span></a>' for key,name in data.items() for view,label,*_ in COMMON_JOBS)
    details=''.join(f'<a href="gilded-{key}.png"><img src="gilded-{key}.png"><span>{label}</span></a>' for key,label,*_ in DETAIL_JOBS)
    html='''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>ArenaGauntlet — Arena cosmetics art review</title><style>
body{margin:0;background:#0b1118;color:#e9e4d9;font:16px system-ui,sans-serif}main{max-width:1440px;margin:auto;padding:32px}h1{font-size:36px;margin:0 0 10px}p{color:#adbdc8;line-height:1.6}h2{margin-top:40px}select{background:#1a2734;color:#f4ecdc;border:1px solid #516577;padding:10px;border-radius:6px;margin:6px}img{max-width:100%;display:block}#compare{position:relative;overflow:hidden;border:1px solid #374958}#right{position:absolute;inset:0;width:100%;height:100%;clip-path:inset(0 0 0 50%)}#divider{position:absolute;top:0;bottom:0;left:50%;width:2px;background:#f5d896;pointer-events:none}input{width:100%;accent-color:#d6b475;margin:14px 0}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(300px,1fr));gap:18px}.grid a{color:#eadfca;text-decoration:none;background:#15202b;border:1px solid #293b4a}.grid span{display:block;padding:12px;font-size:14px}.badge{color:#e4c68e;font-size:13px;letter-spacing:.1em}small{color:#a3b3bd}</style>
<main><div class="badge">ARENAGAUNTLET / ORIGINAL ART REVIEW</div><h1>Three arenas. One proving ground.</h1>
<p>Material skins, matching floors and optional Hellenic decorations. These are studio previews of the authored files, not game screenshots. Default arena files are preserved. Claude will integrate the cosmetic unlocks and selection.</p>
<h2>Compare the same camera</h2><label>Left<select id="leftSelect">OPTIONS</select></label><label>Right<select id="rightSelect">OPTIONS</select></label><label>View<select id="viewSelect">ANGLES</select></label>
<div id="compare"><img id="left"><img id="right"><div id="divider"></div></div><input id="slider" type="range" min="0" max="100" value="50"><small>Move the divider to compare matching views. Click a gallery image for full resolution.</small>
<h2>Skins and matching floors</h2><div class="grid">CARDS</div><h2>Independent decoration sets</h2><div class="grid">DETAILS</div>
<p>All work is art-only. No release, profile installation or game-code changes are included.</p></main><script>
const el=id=>document.getElementById(id);el('rightSelect').value='gilded';function refresh(){let view=el('viewSelect').value;el('left').src=el('leftSelect').value+'-'+view+'.png';el('right').src=el('rightSelect').value+'-'+view+'.png'}['leftSelect','rightSelect','viewSelect'].forEach(id=>el(id).onchange=refresh);el('slider').oninput=()=>{let n=el('slider').value;el('right').style.clipPath=`inset(0 0 0 ${n}%)`;el('divider').style.left=n+'%'};refresh();</script></html>'''
    html=html.replace('OPTIONS',options).replace('ANGLES',angles).replace('CARDS',cards).replace('DETAILS',details)
    (output/'index.html').write_text(html,encoding='utf-8')


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--quick',action='store_true',help='One courtyard and statue view per theme for early review')
    arguments=parser.parse_args()
    catalog=json.loads((ROOT/'assets/arena_cosmetics.json').read_text(encoding='utf-8'))
    output=ROOT/'dist/arena-cosmetics-review';output.mkdir(parents=True,exist_ok=True)
    studio=Studio(catalog)
    jobs=[COMMON_JOBS[2]] if arguments.quick else COMMON_JOBS
    renders=[]
    for theme in ['default']+[theme['id'] for theme in catalog['themes']]:
        title='Original arena' if theme=='default' else next(t['name'] for t in catalog['themes'] if t['id']==theme)
        scene=studio.scene(theme,with_decor=theme!='default')
        theme_jobs=jobs+([DETAIL_JOBS[0]] if arguments.quick and theme!='default' else DETAIL_JOBS if theme=='gilded' and not arguments.quick else [DETAIL_JOBS[1]] if theme!='default' and not arguments.quick else [])
        seen=set()
        for job in theme_jobs:
            if job[0] in seen:continue
            seen.add(job[0]);filename=theme+'-'+job[0]+'.png'
            studio.render(scene,job,title).save(output/filename)
            renders.append(filename);print(filename,flush=True)
    if not arguments.quick:
        (output/'quick-info.json').unlink(missing_ok=True)
        gallery(output,catalog)
        sheet=Image.new('RGB',(1680,746),'#101922')
        for index,theme in enumerate(('gilded','lunar','ruins')):
            for row,view in enumerate(('overview','floor')):
                image=Image.open(output/f'{theme}-{view}.png').resize((560,373),Image.Resampling.LANCZOS)
                sheet.paste(image,(index*560,row*373))
        sheet.save(output/'theme-comparison.png')
    report=dict(renderer=studio.ctx.info['GL_RENDERER'],gameLaunched=False,
        nativeShaderEmulated=False,exactAuthoredGeometry=True,quick=arguments.quick,renders=renders,
        catalogSha256=hashlib.sha256((ROOT/'assets/arena_cosmetics.json').read_bytes()).hexdigest(),
        artInputHashes={path.name:hashlib.sha256(path.read_bytes()).hexdigest()
                        for path in sorted((ROOT/'assets').glob('arena_*')) if path.suffix in ('.png','.meshbin')},
        defaultArenaSha256=hashlib.sha256((ROOT/'assets/colosseum.meshbin').read_bytes()).hexdigest())
    (output/('quick-info.json' if arguments.quick else 'preview-info.json')).write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    studio.ctx.release()


if __name__=='__main__':main()
