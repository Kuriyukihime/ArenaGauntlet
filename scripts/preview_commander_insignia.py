"""Offline studio view of the exact embedded War Council insignia, not a game screenshot."""
from pathlib import Path
import numpy as np,json,hashlib
ROOT=Path(__file__).resolve().parent.parent
source=(ROOT/'scripts/preview_castle.py').read_text(encoding='utf-8').replace("ROOT/'assets/castle.meshbin'","ROOT/'assets/commanders.meshbin'")
# Runtime dyes banner cloth on a plain texture; mirror that for 'cloth' surfaces.
source=source.replace("np.asarray(Image.open(ROOT/f'assets/castle_{name}.png').convert('RGB'),float)/255",
                      "(np.ones((4,4,3)) if name=='cloth' else np.asarray(Image.open(ROOT/f'assets/castle_{name}.png').convert('RGB'),float)/255)")
ns=dict(__file__=str(ROOT/'scripts/preview_castle.py'),__name__='insignia_render_library')
exec(compile(source,'insignia_render_library','exec'),ns)
surfaces,parts=ns['read_asset']()
names=[name for name,_ in surfaces]
# Tier I, III and V standards beside a squad sigil, in their runtime company colors.
companies=[(-2.5,1,(1,.45,.16)),(0,3,(.55,.36,.22)),(2.5,5,(.95,.2,.58))]
shown=[]
def add(part,x,y,color):
    copy=dict(part);copy['v']=part['v'].copy();copy['v'][:,0]+=x;copy['v'][:,1]+=y
    name=names[part['material']]
    if color is not None and name in ('ivory','energy'):
        smooth,glow=surfaces[part['material']][1][3:]
        # Same dye rules as GauntletInsignia.Tinted.
        tint=tuple(c+(1-c)*.55 for c in color) if name=='energy' else tuple(c*.82 for c in color)
        surfaces.append(('cloth' if name=='ivory' else 'energy',(*tint,smooth,glow)));copy['material']=len(surfaces)-1
    shown.append(copy)
for x,tier,color in companies:
    for part in parts:
        if part['name'].split('-')[0] in ('standard','banner',f'rank{tier}'):add(part,x,0,color)
for part in parts:
    if part['name'].startswith('sigil'):add(part,4.4,1.2,(.3,.9,1))
out=ROOT/'dist/commander-insignia-review';out.mkdir(parents=True,exist_ok=True)
ns['render'](shown,surfaces,[0]*26,out/'01-insignia.png','CASTLE GAUNTLET / WAR COUNCIL INSIGNIA',yaw=24,pitch=8,size=(1440,900))
report=dict(actualRuntimeGeometry=True,gameLaunched=False,source='assets/commanders.meshbin',
            shown=['Cinder Warband tier I','Clay Templars tier III','Abyssal Siege tier V','Solus Directive squad sigil'],
            sha256=hashlib.sha256((ROOT/'assets/commanders.meshbin').read_bytes()).hexdigest())
(out/'preview-info.json').write_text(json.dumps(report,indent=2))
