"""Original Hellenic arena, authored as the exact CAST1 mesh shipped in the mod.
Geometry-first, as in Ivory Bastion: bevels, carved relief, layered construction
and original stone surfaces. No downloaded models or copied game art.
"""
from pathlib import Path
from collections import OrderedDict
import math,struct,json,hashlib
import numpy as np
from PIL import Image
from build_castle_models import Geometry,PARTS,SURFACES,string,rotation,unit
ROOT=Path(__file__).resolve().parent.parent
PARTS.clear();SURFACES.clear()
SURFACES.update(OrderedDict([
 ('limestone',((1.,.98,.93),.12,0)),('marble',((1.,1.,.98),.21,0)),
 ('paving',((1.,.98,.93),.09,0)),('basalt',((.85,.90,.88),.11,0)),
 ('roofstone',((1.,.98,.94),.13,0)),('grass',((.90,.96,.86),.025,0))]))
stadium=Geometry(0,'Stadium');peristyle=Geometry(0,'Peristyle')
temple=Geometry(0,'Propylon');court=Geometry(0,'Courtyard')
counts=dict(doricColumns=0,flutesPerColumn=20,pedimentedGateways=4,seatingRows=18,radialAisles=12,grassBeds=12)

def polar(r,y,deg):
 a=math.radians(deg);return np.array((r*math.sin(a),y,r*math.cos(a)))

def sector(g,mat,r0,r1,y0,y1,a0,a1,steps=1):
 for i in range(steps):
  a=a0+(a1-a0)*i/steps;b=a0+(a1-a0)*(i+1)/steps
  def p(r,y,t):return (r*math.sin(t),y,r*math.cos(t))
  if r0==0:
   g.face(mat,[(0,y1,0),p(r1,y1,b),p(r1,y1,a)],(0,1,0))
   g.face(mat,[(0,y0,0),p(r1,y0,a),p(r1,y0,b)],(0,-1,0))
  else:
   g.face(mat,[p(r0,y1,a),p(r0,y1,b),p(r1,y1,b),p(r1,y1,a)],(0,1,0))
   g.face(mat,[p(r0,y0,a),p(r1,y0,a),p(r1,y0,b),p(r0,y0,b)],(0,-1,0))
  # Flush courtyard faces are decorative, never additional collision solids.
  if g is court:continue
  mid=(a+b)*.5;out=(math.sin(mid),0,math.cos(mid))
  for r,n in ((r0,tuple(-v for v in out)),(r1,out)):
   if r:g.face(mat,[p(r,y0,a),p(r,y1,a),p(r,y1,b),p(r,y0,b)],n)
  for t,n in ((a,(-math.cos(a),0,math.sin(a))),(b,(math.cos(b),0,-math.sin(b)))):
   g.face(mat,[p(r0,y0,t),p(r0,y1,t),p(r1,y1,t),p(r1,y0,t)],n)

def ring(g,mat,r0,r1,y0,y1,gates=False,segments=192):
 for i in range(segments):
  a=(i+.5)*math.tau/segments
  if gates and min(abs(math.sin(a)),abs(math.cos(a)))*r0<33:continue
  sector(g,mat,r0,r1,y0,y1,i*math.tau/segments,(i+1)*math.tau/segments)

def column(g,center,height,radius,yaw=0,detail=True):
 """Twenty concave flutes, entasis, tapered shaft, annulets and Doric capital."""
 counts['doricColumns']+=1;c=np.asarray(center);rot=rotation((0,yaw,0));sides=60
 profile=[(0,1),(.32,1.015),(.70,.95),(1,.86)]
 bottom=.25;shaft=height-1.55;rings=[]
 for t,scale in profile:
  pts=[]
  for j in range(sides):
   a=j*math.tau/sides;r=radius*scale*(1-.065*(.5-.5*math.cos(a*20)))
   pts.append((r*math.cos(a),bottom+t*shaft,r*math.sin(a)))
  rings.append(pts)
 for lower,upper in zip(rings,rings[1:]):
  for j in range(sides):
   pts=[lower[j],upper[j],upper[(j+1)%sides],lower[(j+1)%sides]]
   want=np.mean(pts,axis=0);want[1]=0;g.face('marble',pts,want,(c,rot))
 if detail:
  for t in (.27,.54,.81):
   r=radius*(1.015-.12*t)
   g.lathe('limestone',c,[(r,bottom+t*shaft-.014),(r,bottom+t*shaft+.014)],40,caps=False)
 neck=bottom+shaft
 for dy in (0,.13,.26):
  g.lathe('marble',c,[(radius*.88,neck+dy),(radius*.94,neck+dy+.045),
                     (radius*.94,neck+dy+.10),(radius*.88,neck+dy+.13)],24,caps=False)
 g.lathe('marble',c,[(radius*.89,neck+.38),(radius*.96,neck+.48),(radius*1.17,neck+.72),
                    (radius*1.23,neck+.84),(radius*1.23,height-.31)],24,caps=False)
 g.box('marble',c+(0,height-.16,0),(radius*2.75,.32,radius*2.75),.065,(0,yaw,0))
 g.lathe('marble',c,[(radius*1.025,0),(radius*1.025,.13),(radius,.25)],24,caps=False)

def wreath(g,origin,center,radius,yaw):
 """Sculpted laurel leaves, each with a raised central rib, rather than decals."""
 rot=rotation((0,yaw,0));c=np.asarray(center)
 for side in (-1,1):
  for j in range(9):
   a=-math.pi*.44+j*math.pi*.105;p=c+np.array((side*radius*math.cos(a),radius*math.sin(a),0))
   d=unit((side*math.cos(a+.65),math.sin(a+.65),0));s=np.array((-d[1],d[0],0))
   length=.90*radius/2.7;width=.34*radius/2.7
   pts=[p-d*length*.5,p-d*length*.12+s*width*.5,p+d*length*.34+s*width*.36,
        p+d*length*.5,p+d*length*.34-s*width*.36,p-d*length*.12-s*width*.5]
   ridge=p+(0,0,-.11)
   for k in range(6):g.face('marble',[pts[k],pts[(k+1)%6],ridge],(0,0,-1),(origin,rot))

# The collider stays one uninterrupted plane at y=0. All courtyard art is flush.
ring(court,'paving',0,179,-.10,0)
ring(court,'basalt',175.6,177.1,.002,.010);ring(court,'marble',177.2,178.6,.001,.012)
ring(court,'basalt',20.1,21.4,.002,.012);ring(court,'marble',21.5,22.2,.001,.014)
ring(court,'marble',0,12,-.025,.008)
for deg in range(0,360,45):
 court.face('basalt',[polar(2,.018,deg-24),polar(10.7,.018,deg),polar(2,.018,deg+24)],(0,1,0))
# Engraved Greek-key border in dark stone, with no movement barrier.
for i in range(160):
 a=i*math.tau/160;b=(i+.78)*math.tau/160
 for r0,r1,aa,bb in [(171.8,172.15,a,b),(173.35,173.7,a,b),(172.15,173.35,a,a+.0026),
                     (172.15,173.35,b-.0026,b),(172.65,172.95,a+.003,b-.003)]:
  sector(court,'basalt',r0,r1,.001,.014,aa,bb)
for i in range(12):
 start=math.radians(i*30+7.5);end=start+math.radians(15)
 sector(court,'grass',123,169,.001,.015,start,end,12)
 for r0,r1 in ((122.4,123),(169,169.65)):sector(court,'marble',r0,r1,.001,.023,start,end,12)
 for a,b in ((start-.003,start),(end,end+.003)):sector(court,'marble',122.4,169.65,.001,.023,a,b,12)

# Limestone cavea, with radial stair aisles and four broad entrance passages.
ring(stadium,'limestone',179,241,-6,-.05)
for row in range(18):
 inner=184+row*2.3;top=2.5+row*1.03
 for j in range(192):
  deg=(j+.5)*360/192
  if min(abs(math.sin(math.radians(deg))),abs(math.cos(math.radians(deg))))*inner<33:continue
  if abs((deg+15)%30-15)<1.25:continue
  a=j*math.tau/192;b=(j+1)*math.tau/192
  sector(stadium,'limestone',inner,inner+2.3,0,top,a,b)
  sector(stadium,'marble',inner,inner+.25,top,top+.14,a,b)
 for deg in range(30,360,30):
  if deg%90==0:continue
  for half in range(2):
   r=inner+.575+half*1.15;y=top-.515+half*.515
   stadium.box('limestone',polar(r,y-.16,deg),(7.2,.32,1.18),.045,(0,deg,0))
ring(stadium,'marble',179.5,183.7,0,2.5,True)
ring(stadium,'basalt',179.3,179.55,.2,.70,True);ring(stadium,'marble',179.15,184,2.5,2.85,True)
ring(stadium,'limestone',225.4,240.5,0,21.15,True);ring(stadium,'marble',225.1,241,21.15,21.55,True)
for course in range(9):
 y=course*2.3
 for j in range(96):
  deg=(j+.5*(course%2))*3.75
  if abs((deg+45)%90-45)<8.75:continue
  sector(stadium,'limestone',239.8,240.8,y+.025,y+2.26,math.radians(deg-1.82),math.radians(deg+1.82))

# Open Doric peristyle, replacing the old Roman arcades.
for j in range(96):
 deg=j*3.75
 if abs((deg+45)%90-45)<8.75:continue
 column(peristyle,polar(232.4,21.55,deg),9.3,.77,deg,False)
for r0,r1,y0,y1,mat in [(229.5,235.3,30.85,31.40,'marble'),(229.2,235.6,31.40,32.65,'limestone'),
                       (228.9,235.9,32.65,33.10,'marble'),(228.6,236.2,33.10,33.45,'marble')]:
 ring(peristyle,mat,r0,r1,y0,y1,True)
for j in range(192):
 deg=j*1.875
 if abs((deg+45)%90-45)<8.75:continue
 peristyle.box('marble',polar(228.92,32.02,deg),(.48,1.12,.16),.025,(0,deg,0))
 for x in (-.14,0,.14):
  pos=polar(228.81,32.02,deg)+rotation((0,deg,0))@np.array((x,0,0))
  peristyle.box('basalt',pos,(.045,.84,.025),.005,(0,deg,0))
 peristyle.box('marble',polar(228.5,32.95,deg+.72),(.32,.29,.55),.035,(0,deg,0))

for deg in (0,90,180,270):
 c=polar(222,0,deg);rot=rotation((0,deg,0))
 def pos(x,y,z):return c+rot@np.array((x,y,z))
 for width,depth,y in ((57,25,-.2),(55.6,23.8,-.1),(54.5,22.8,0)):
  temple.box('limestone',pos(0,y-.13,0),(width,.26,depth),.055,(0,deg,0))
 for z,xs in [(-9.4,(-25,-18,-10,10,18,25)),(9.4,(-25,-10,10,25))]:
  for x in xs:column(temple,pos(x,.15,z),18.0,1.20,deg)
 temple.box('marble',pos(0,18.75,0),(55.6,1.2,23.1),.15,(0,deg,0))
 temple.box('limestone',pos(0,20.05,0),(56,1.4,23.5),.12,(0,deg,0))
 temple.box('marble',pos(0,21.13,0),(57.2,.72,24.4),.13,(0,deg,0))
 temple.box('marble',pos(0,21.62,0),(58,.28,25),.06,(0,deg,0))
 for z,sign in ((-11.86,-1),(11.86,1)):
  for x in np.arange(-26.4,27,3.3):
   temple.box('marble',pos(x,20.05,z),(.78,1.22,.18),.035,(0,deg,0))
   for dx in (-.23,0,.23):temple.box('basalt',pos(x+dx,20.05,z+sign*.105),(.065,.92,.028),.006,(0,deg,0))
  for x in np.arange(-27.3,28,1.1):temple.box('marble',pos(x,21.43,z+sign*.4),(.42,.28,.75),.035,(0,deg,0))
 base=21.86;rise=7.9;half=29.6
 for z,n in ((-12.6,(0,0,-1)),(12.6,(0,0,1))):
  temple.face('limestone',[(-half,base,z),(half,base,z),(0,base+rise,z)],n,(c,rot))
 slope=math.degrees(math.atan2(rise,half));length=math.hypot(half,rise)
 for side in (-1,1):
  for z in (-12.7,12.7):temple.box('marble',pos(side*half*.5,base+rise*.5,z),(length+.65,.65,.8),.11,(0,deg,-side*slope))
  temple.face('roofstone',[(side*(half+.5),base+.45,-12.9),(side*(half+.5),base+.45,12.9),
                          (0,base+rise+.45,12.9),(0,base+rise+.45,-12.9)],(side*.3,1,0),(c,rot))
  for z in np.arange(-12.4,12.5,2.5):temple.box('roofstone',pos(side*half*.5,base+rise*.5+.55,z),(length+.4,.10,.15),.025,(0,deg,-side*slope))
 temple.box('marble',pos(0,base+rise+.65,0),(.5,.40,26.2),.09,(0,deg,0))
 temple.lathe('marble',pos(0,25.2,-12.72),[(1.75,-.08),(1.91,0),(1.91,.10),(1.65,.24),(.25,.40)],48,(90,deg,0))
 wreath(temple,c,(0,25.1,-12.88),3.3,deg)
 for x in (-27.6,0,27.6):
  yy=base+rise+.8 if x==0 else base+1.0
  temple.lathe('marble',pos(x,yy,0),[(.6,0),(.73,.15),(.52,.4),(.4,1.1),(.08,1.65)],24)

# Five recessed merchant bays occupy the already-open southern entrance.
# Nothing is built in the combat/navigation circle (radius 168m).
counts['merchantAlcoves']=5
shutters=Geometry(0,'MerchantShutters')
for x in (-24,-12,0,12,24):
 for side in (-1,1):
  column(temple,(x+side*5.15,0,-175),7.8,.48,180)
  temple.box('limestone',(x+side*5.55,4,-183),(1.0,8,15),.09)
 for row in range(8):
  for j in range(4):
   xx=x-4.1+j*2.7+(row%2)*.12
   temple.box('limestone',(xx,.5+row,-190),(2.66,.96,.72),.035)
 temple.box('marble',(x,8.1,-182),(11.7,.60,16.5),.10)
 temple.box('limestone',(x,8.65,-182),(11.9,.50,16.8),.08)
 temple.box('marble',(x,9,-182),(12.1,.24,17),.055)
 # Carved lintel, dentils and a laurel medallion over the open shop front.
 temple.box('basalt',(x,6.85,-174.7),(8.5,1.2,.22),.06)
 for dx in np.arange(-4.8,5,.65):temple.box('marble',(x+dx,7.65,-174.45),(.25,.26,.48),.035)
 temple.lathe('marble',(x,8.48,-173.78),[(.48,-.06),(.54,0),(.54,.10),(.42,.20)],24,(90,0,0))
 # Decorative rear shelves leave both rows of terminals easy to approach.
 for y in (1.8,3.6):temple.box('marble',(x,y,-189),(8.8,.20,1.0),.04)
 for panel in range(5):
  shutters.box('basalt',(x+(panel-2)*1.78,3.2,-174.7),(1.74,6.3,.24),.06)
  shutters.box('marble',(x+(panel-2)*1.78,3.2,-174.52),(.12,5.9,.08),.025)
# Ready brazier, on the broad south approach in front of the arcade.
temple.lathe('basalt',(0,0,-171),[(1.25,0),(1.35,.15),(1.05,.4),(.72,1.3),(.94,1.5),(.94,1.65)],32)
temple.lathe('marble',(0,0,-171),[(1.05,1.65),(1.4,1.9),(1.42,2.25),(1.25,2.4)],32)

def textures():
 """Deterministic cut stone, bevel wear, pores and mineral veins, plus AG normals."""
 n=1024;yy,xx=np.mgrid[:n,:n];rng=np.random.default_rng(170017)
 bases={'limestone':(204,195,173),'marble':(225,223,211),'paving':(191,184,164),'basalt':(73,84,79),'roofstone':(184,177,157)}
 for name,base in bases.items():
  coarse=np.asarray(Image.fromarray(np.uint8(rng.uniform(0,255,(32,32)))).resize((n,n),Image.Resampling.BICUBIC),float)/255
  tone=(coarse-.5)*13+rng.normal(0,1.6,(n,n))
  if name=='marble':tone-=np.exp(-((np.sin(xx*.009+yy*.004+coarse*1.4))/.07)**2)*11
  if name in ('paving','roofstone'):
   rows=6 if name=='paving' else 3;cols=4 if name=='paving' else 3;row=yy*rows//n
   horizontal=yy%(n/rows);u=(xx+(row%2)*n/cols*.5)%(n/cols)
   edge=np.minimum.reduce([u,n/cols-u,horizontal,n/rows-horizontal])
   joint=np.clip(edge/3,0,1);bevel=np.clip((edge-3)/6,0,1);block=rng.uniform(-8,8,(rows,cols+1))
   col=((xx+(row%2)*n/cols*.5)*cols//n).astype(int)%(cols+1);tone+=block[row,col]
   color=(np.array(base)[None,None,:]+tone[:,:,None])*(.48+.52*joint[:,:,None])+(1-bevel[:,:,None])*joint[:,:,None]*12
  else:color=np.array(base)[None,None,:]+tone[:,:,None]
  pits=rng.random((n,n))<.006;color[pits]*=.78
  image=Image.fromarray(np.uint8(np.clip(color,0,255)));image.save(ROOT/f'assets/arena_{name}.png',optimize=True)
  height=np.asarray(image.convert('L'),float)/255;gy,gx=np.gradient(height)
  normal=np.dstack((-gx*2.2,-gy*2.2,np.ones_like(gx)));normal/=np.linalg.norm(normal,axis=2,keepdims=True)
  packed=np.zeros((n,n,4),np.uint8);packed[:,:,0]=255;packed[:,:,2]=255
  packed[:,:,1]=np.uint8((normal[:,:,1]*.5+.5)*255);packed[:,:,3]=np.uint8((normal[:,:,0]*.5+.5)*255)
  Image.fromarray(packed).save(ROOT/f'assets/arena_{name}_normal.png',optimize=True)

def write():
 path=ROOT/'assets/colosseum.meshbin'
 for key,part in PARTS.items():
  if key[-1] in ('paving','grass'):
   scale=12 if key[-1]=='paving' else 18
   for v in part['v']:v[6]=v[0]/scale;v[7]=v[2]/scale
 # Weld identical complete vertices (including normals/UVs), preserving hard
 # bevel edges. This lowers upload/RAM cost without changing a single triangle.
 for part in PARTS.values():
  vertices=np.asarray(part['v'],dtype='<f4')
  unique,inverse=np.unique(vertices,axis=0,return_inverse=True)
  part['v']=unique;part['t']=inverse[np.asarray(part['t'],dtype=np.int32)]
 with path.open('wb') as f:
  f.write(b'CAST1');f.write(struct.pack('<i',len(SURFACES)))
  for name,(color,smooth,glow) in SURFACES.items():string(f,name);f.write(struct.pack('<5f',*color,smooth,glow))
  f.write(struct.pack('<i',len(PARTS)))
  for (module,label,conditions,attachment,ruin,mat),part in PARTS.items():
   string(f,label+'-'+mat);f.write(struct.pack('<iii?i',0,0,list(SURFACES).index(mat),False,0))
   f.write(struct.pack('<ii',len(part['v']),len(part['t'])*3))
   f.write(np.asarray(part['v'],dtype='<f4').tobytes());f.write(np.asarray(part['t'],dtype='<i4').tobytes())
 report=dict(authoring='scripts/build_colosseum.py',originalGeometry=True,style='Hellenic / Doric sanctuary stadium',detailLevels=1,
             parts=len(PARTS),triangles=sum(len(p['t']) for p in PARTS.values()),vertices=sum(len(p['v']) for p in PARTS.values()),
             combatRadius=178,architectureRadius=[179,242],**counts,materialNames=list(SURFACES),modelSha256=hashlib.sha256(path.read_bytes()).hexdigest())
 (ROOT/'assets/colosseum-info.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report),flush=True)

if __name__=='__main__':textures();write()
