"""Original War Council insignia, authored with the colosseum's beveled-mesh workflow.
A commander carries a war standard whose banner shows its tier in chevrons; squad
members wear a floating sigil. Banner cloth and glows are tinted per company at
runtime. The CAST1 binary is embedded unchanged.
"""
from pathlib import Path
import math,struct,json
import numpy as np
from build_castle_models import Geometry,PARTS,SURFACES,string
ROOT=Path(__file__).resolve().parent.parent
PARTS.clear()
# Standard: a brass pole, crossbar and finial, sized for the runtime's head offset.
pole=Geometry(0,'standard')
pole.lathe('brass',(0,0,0),[(.07,0),(.07,.06),(.045,.1),(.045,1.86),(.06,1.9),(.06,1.94)],sides=16)
pole.box('brass',(0,1.78,0),(1.02,.07,.07),.02)
for side in (-1,1):pole.gem('brass',(side*.55,1.78,0),.055,.16,(0,0,90),6)
pole.gem('brass',(0,2.1,0),.09,.32,(0,0,0),8)
pole.ring('brass',(0,1.94,0),.11,.022,(0,0,0),24)
pole.ring('brass',(0,.95,0),.07,.018,(0,0,0),20)
# Swallowtail banner hangs in front of the pole; a thin slab reads from both
# sides. Two convex halves keep the tail notch open on either face.
cloth=Geometry(0,'banner');FRONT,BACK=.095,.063
halves=[[(-.44,1.73),(0,1.73),(0,.84),(-.44,.56)],[(0,1.73),(.44,1.73),(.44,.56),(0,.84)]]
for half in halves:
    for z,normal in ((FRONT,(0,0,1)),(BACK,(0,0,-1))):cloth.face('ivory',[(x,y,z) for x,y in half],normal)
outline=[(-.44,1.73),(.44,1.73),(.44,.56),(0,.84),(-.44,.56)]
for (x0,y0),(x1,y1) in zip(outline,outline[1:]+outline[:1]):
    edge=np.array((y1-y0,-(x1-x0),0.))
    cloth.face('ivory',[(x0,y0,BACK),(x1,y1,BACK),(x1,y1,FRONT),(x0,y0,FRONT)],edge)
cloth.box('brass',(0,1.72,(FRONT+BACK)/2),(.94,.05,.07),.015)
# Tier chevrons glow in the company color on both banner faces.
for tier in range(1,6):
    rank=Geometry(0,f'rank{tier}')
    for i in range(tier):
        y=1.52-i*.16
        for z in (FRONT+.012,BACK-.012):
            for side in (-1,1):
                rank.box('energy',(side*.115,y,z),(.27,.045,.02),.008,(0,0,side*-32))
# Squad sigil: a floating brass halo around a company-colored crystal.
sigil=Geometry(0,'sigil')
sigil.ring('brass',(0,0,0),.24,.03,(90,0,0),28)
sigil.ring('brass',(0,0,0),.24,.02,(0,0,0),28)
sigil.gem('energy',(0,0,0),.12,.36,(0,0,0),6)
path=ROOT/'assets/commanders.meshbin'
with path.open('wb') as f:
    f.write(b'CAST1');f.write(struct.pack('<i',len(SURFACES)))
    for name,(color,smooth,glow) in SURFACES.items():string(f,name);f.write(struct.pack('<5f',*color,smooth,glow))
    f.write(struct.pack('<i',len(PARTS)))
    for (module,label,conditions,attachment,ruin,mat),p in PARTS.items():
        string(f,label+'-'+mat);f.write(struct.pack('<iii?i',0,0,list(SURFACES).index(mat),False,0))
        f.write(struct.pack('<ii',len(p['v']),len(p['t'])*3))
        f.write(np.asarray(p['v'],dtype='<f4').tobytes());f.write(np.asarray(p['t'],dtype='<i4').tobytes())
report=dict(authoring='scripts/build_commander_insignia.py',originalGeometry=True,detailLevels=1,
            parts=[label+'-'+mat for (module,label,conditions,attachment,ruin,mat) in PARTS],
            triangles=sum(len(p['t']) for p in PARTS.values()),standardHeight=2.26,bannerWidth=.88,sigilRadius=.27)
(ROOT/'assets/commanders-info.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report))
