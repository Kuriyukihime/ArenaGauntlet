"""Author the actual tiered citadel meshes, following the Seraph mesh workflow.

Original geometry only. No AI concept images, downloaded models or game meshes.
The CAST1 output is the asset embedded in the mod and read by preview_castle.py.
"""
from pathlib import Path
from collections import OrderedDict
import math, struct, json
import numpy as np
from PIL import Image, ImageFilter

ROOT=Path(__file__).resolve().parent.parent
OUT=ROOT/'assets'
OUT.mkdir(exist_ok=True)
SURFACES=OrderedDict([
    ('ivory',((.76,.80,.79),.22,0)),
    ('stone',((.38,.46,.49),.18,0)),
    ('alloy',((.055,.085,.11),.42,0)),
    ('slate',((.095,.23,.30),.35,0)),
    ('brass',((.66,.47,.23),.58,0)),
    ('energy',((.18,.76,1.),.35,1.8)),
    ('glass',((.035,.17,.23),.62,.08)),
    ('green',((.25,.82,.50),.30,.65)),
    ('red',((.97,.39,.25),.30,.65)),
])
NAMES=['Masonry','Reinforcement','Ramparts','Ballista','Artillery','Frost','Market','Workshop','WhitePrinter','GreenPrinter','RedPrinter','Forge','Reforge','Sanctum','Cleansing','Order','Recycler','Hangar','DroneUpgrade','DroneCombiner','DroneScrapper','Archive','ArchiveGreen','Kitchen','Donation','GoldenVault']
MAXIMA=[5,3,3,3,3,3,3,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1]
PARTS=OrderedDict()

def unit(v):
    v=np.asarray(v,float)
    return v/max(np.linalg.norm(v),1e-12)

def rotation(angles):
    x,y,z=np.radians(angles)
    rx=np.array([[1,0,0],[0,math.cos(x),-math.sin(x)],[0,math.sin(x),math.cos(x)]])
    ry=np.array([[math.cos(y),0,math.sin(y)],[0,1,0],[-math.sin(y),0,math.cos(y)]])
    rz=np.array([[math.cos(z),-math.sin(z),0],[math.sin(z),math.cos(z),0],[0,0,1]])
    return ry@rx@rz

class Geometry:
    def __init__(self,module,label,conditions=(),attachment=0,ruin=False):
        self.module=module;self.label=label;self.conditions=tuple(conditions);self.attachment=attachment;self.ruin=ruin
    def face(self,mat,points,want=None,transform=None):
        points=np.asarray(points,float)
        if transform is not None:
            origin,rot=transform;points=points@rot.T+origin
            if want is not None:want=rot@np.asarray(want)
        normal=np.cross(points[1]-points[0],points[2]-points[0])
        if np.linalg.norm(normal)<1e-10:return
        if want is not None and np.dot(normal,want)<0:points=points[::-1];normal=-normal
        normal=unit(normal)
        axis=np.argmax(np.abs(normal))
        uv=points[:,(2,1) if axis==0 else (0,2) if axis==1 else (0,1)]*.47
        key=(self.module,self.label,self.conditions,self.attachment,self.ruin,mat)
        part=PARTS.setdefault(key,{'v':[],'t':[]})
        start=len(part['v'])
        part['v'].extend(np.column_stack((points,np.tile(normal,(len(points),1)),uv)).tolist())
        part['t'].extend((start,start+i,start+i+1) for i in range(1,len(points)-1))
    def box(self,mat,center,size,bevel=.08,angles=(0,0,0)):
        sx,sy,sz=np.asarray(size,float)*.5;b=min(bevel,sx*.35,sy*.35,sz*.35)
        rings=[]
        for y,inset in [(-sy,b),(-sy+b,0),(sy-b,0),(sy,b)]:
            x,z=sx-inset,sz-inset;c=max(.001,b*.7)
            rings.append([(px,y,pz) for px,pz in [(-x+c,-z),(x-c,-z),(x,-z+c),(x,z-c),(x-c,z),(-x+c,z),(-x,z-c),(-x,-z+c)]])
        tr=(np.asarray(center),rotation(angles))
        self.face(mat,rings[0],(0,-1,0),tr);self.face(mat,rings[-1],(0,1,0),tr)
        for a,bottom in enumerate(rings[:-1]):
            top=rings[a+1]
            for j in range(8):
                pts=[bottom[j],top[j],top[(j+1)%8],bottom[(j+1)%8]]
                want=np.mean(pts,axis=0);want[1]=0
                # Sloped edge faces also retain their exact beveled normal.
                self.face(mat,pts,want,tr)
    def lathe(self,mat,center,profile,sides=40,angles=(0,0,0),caps=True):
        tr=(np.asarray(center),rotation(angles));rings=[]
        for radius,y in profile:
            rings.append([(radius*math.cos(a),y,radius*math.sin(a)) for a in np.arange(sides)*math.tau/sides])
        if caps:
            self.face(mat,rings[0],(0,-1,0),tr);self.face(mat,rings[-1],(0,1,0),tr)
        for layer,(lower,upper) in enumerate(zip(rings,rings[1:])):
            dr=profile[layer+1][0]-profile[layer][0];dy=profile[layer+1][1]-profile[layer][1]
            for j in range(sides):
                pts=[lower[j],upper[j],upper[(j+1)%sides],lower[(j+1)%sides]]
                angle=(j+.5)*math.tau/sides
                self.face(mat,pts,(dy*math.cos(angle),-dr,dy*math.sin(angle)),tr)
    def ring(self,mat,center,radius,tube=.07,angles=(0,0,0),sides=48):
        tr=(np.asarray(center),rotation(angles));minor=8
        for i in range(sides):
            a0=i*math.tau/sides;a1=(i+1)*math.tau/sides
            for j in range(minor):
                b0=j*math.tau/minor;b1=(j+1)*math.tau/minor
                def p(a,b):return ((radius+tube*math.cos(b))*math.cos(a),tube*math.sin(b),(radius+tube*math.cos(b))*math.sin(a))
                points=[p(a0,b0),p(a0,b1),p(a1,b1),p(a1,b0)]
                mid=np.mean(points,axis=0);radial=unit((mid[0],0,mid[2]));want=mid-radial*radius
                self.face(mat,points,want,tr)
    def gem(self,mat,center,radius,height,angles=(0,0,0),sides=6):
        tr=(np.asarray(center),rotation(angles));ring=[(radius*math.cos(a),0,radius*math.sin(a)) for a in np.arange(sides)*math.tau/sides]
        for i in range(sides):
            for end in [(0,-height*.5,0),(0,height*.5,0)]:
                pts=[ring[i],ring[(i+1)%sides],end]
                self.face(mat,pts,np.mean(pts,axis=0),tr)
    def arch(self,mat,center,width,height,frame=.18,depth=.18,angles=(0,0,0)):
        tr=(np.asarray(center),rotation(angles));spring=height-width*.5
        for side in (-1,1):
            c=np.asarray(center)+tr[1]@np.array((side*(width*.5+frame*.5),spring*.5,0))
            self.box(mat,c,(frame,spring+frame,depth),min(frame*.2,.06),angles)
        for i in range(20):
            a=i*math.pi/20;b=(i+1)*math.pi/20
            def p(angle,r,z):return (r*math.cos(angle),spring+r*math.sin(angle),z)
            for z,normal in [(-depth*.5,(0,0,-1)),(depth*.5,(0,0,1))]:
                self.face(mat,[p(a,width*.5,z),p(b,width*.5,z),p(b,width*.5+frame,z),p(a,width*.5+frame,z)],normal,tr)
            for r,sign in [(width*.5,-1),(width*.5+frame,1)]:
                pts=[p(a,r,-depth*.5),p(b,r,-depth*.5),p(b,r,depth*.5),p(a,r,depth*.5)]
                self.face(mat,pts,(sign*math.cos((a+b)*.5),sign*math.sin((a+b)*.5),0),tr)
    def window(self,center,width=1,height=1.6,angles=(0,0,0),glow='energy'):
        rot=rotation(angles);c=np.asarray(center)
        self.box('alloy',c,(width+.3,height+.24,.14),.07,angles)
        self.box('glass',c+rot@np.array((0,0,.085)),(width,height,.04),.035,angles)
        for x in (-width*.22,width*.22):self.box(glow,c+rot@np.array((x,0,.12)),(.065,height*.78,.035),.012,angles)
        self.box('brass',c+rot@np.array((0,0,.14)),(width,.065,.04),.015,angles)
    def roof(self,center,width,depth,height=1.25):
        c=np.asarray(center);x,z=width*.5,depth*.5
        a,b,e,f=[np.array(p)+c for p in [(-x,0,-z),(x,0,-z),(-x,0,z),(x,0,z)]]
        r0=c+(0,height,-z+.32);r1=c+(0,height,z-.32)
        self.face('slate',[a,r0,r1,e],(-1,1,0));self.face('slate',[b,f,r1,r0],(1,1,0))
        self.face('slate',[a,b,r0],(0,0,-1));self.face('slate',[e,r1,f],(0,0,1))
        self.box('alloy',c-(0,.08,0),(width+.18,.2,depth+.18),.06)
        self.box('brass',c+(0,height+.04,0),(.16,.16,depth-.6),.035)
        pitch=math.degrees(math.atan2(height,x));length=math.hypot(x,height)
        for zz in np.arange(-z+.4,z,.85):
            for side in (-1,1):self.box('alloy',c+(side*x*.5,height*.5+.035,zz),(length,.065,.065),.018,(0,0,-side*pitch))
    def fin(self,center,width=1,height=1.5,angles=(0,0,0),mat='ivory'):
        c=np.asarray(center);rot=rotation(angles)
        outline=[(-width*.5,0),(-width*.34,height*.83),(0,height),(width*.34,height*.83),(width*.5,0)]
        for z,n in [(-.10,(0,0,-1)),(.10,(0,0,1))]:self.face(mat,[(x,y,z) for x,y in outline],n,(c,rot))
        for i,(x,y) in enumerate(outline):
            xx,yy=outline[(i+1)%len(outline)]
            self.face('brass',[(x,y,-.10),(xx,yy,-.10),(xx,yy,.10),(x,y,.10)],None,(c,rot))

def condition(name,rank=1,maximum=99):return (NAMES.index(name),rank,maximum)
def group(module,label,*conditions,attachment=0,ruin=False):return Geometry(module,label,conditions,attachment,ruin)

def keep():
    g=group(0,'keep-base')
    g.box('stone',(0,.12,0),(9.8,.35,9.8),.14)
    g.box('alloy',(0,.39,0),(8.5,.25,8.5),.11)
    g.box('ivory',(0,1.65,0),(6.9,2.35,6.9),.22)
    g.box('stone',(0,2.95,0),(7.25,.38,7.25),.14)
    for x in (-2.65,2.65):
        for z in (-2.65,2.65):
            g.lathe('ivory',(x,0,z),[(.72,.4),(.72,1),(.49,1.15),(.49,5.55),(.64,5.75),(.64,6)],16)
            g.lathe('brass',(x,0,z),[(.51,1.3),(.57,1.32),(.57,1.48),(.51,1.50)],16)
            g.box('energy',(x,3.7,z-.5),(.09,2.7,.045),.01)
    g.lathe('alloy',(0,0,0),[(3.1,3.13),(3.22,3.25),(3.22,3.39),(3.1,3.45)],32)
    # An open structural crown, not a solid disc hiding the reactor.
    g.lathe('ivory',(0,0,0),[(3.1,5.95),(3.84,5.95),(3.93,6.06),(3.93,6.28),(3.84,6.37),(3.1,6.37),(3.1,5.95)],24,caps=False)
    g.lathe('brass',(0,0,0),[(3.75,6.38),(3.9,6.38),(3.9,6.48),(3.75,6.48),(3.75,6.38)],24,caps=False)
    for side in range(4):
        yaw=side*90;rot=rotation((0,yaw,0));base=rot@np.array((0,1.9,3.51))
        g.window(base,1.4,1.3,(0,yaw,0))
        for offset in (-2.1,2.1):g.box('stone',rot@np.array((offset,1.55,3.48)),(.21,2.0,.19),.045,(0,yaw,0))
    r=group(0,'suspended-heart',attachment=2)
    r.gem('energy',(0,0,0),1.1,2.7,(0,30,0),8)
    r.ring('brass',(0,-.75,0),1.28,.09,sides=48);r.ring('alloy',(0,.72,0),1.28,.10,sides=48)
    for i in range(4):r.gem('glass',(math.sin(i*math.pi/2)*1.37,0,math.cos(i*math.pi/2)*1.37),.15,1.1)
    for rank in range(1,6):
        g=group(0,'foundation-tier-'+str(rank),condition('Masonry',rank))
        if rank==1:
            for yaw in (0,90,180,270):
                rot=rotation((0,yaw,0));g.box('ivory',rot@np.array((0,.76,4.0)),(4.7,.55,1.15),.14,(0,yaw,0))
                g.box('brass',rot@np.array((0,1.05,4.3)),(3.8,.11,.1),.025,(0,yaw,0))
        elif rank==2:
            for x in (-3.1,3.1):
                for z in (-3.1,3.1):
                    g.box('ivory',(x,3.65,z),(.44,3.1,.44),.12);g.gem('energy',(x,5.34,z),.15,.42)
        elif rank==3:
            for x in (-3.85,3.85):
                for z in (-3.85,3.85):
                    g.lathe('stone',(x,0,z),[(.78,.1),(.78,.6),(.52,.8),(.52,3.5),(.72,3.65),(.72,3.85)],16)
                    g.gem('brass',(x,4.35,z),.68,1.0,sides=8)
        elif rank==4:
            for yaw in (0,90,180,270):
                rot=rotation((0,yaw,0));p=rot@np.array((0,6.10,2.95))
                g.box('ivory',p,(4.6,.6,1.4),.16,(0,yaw,0))
                for xx in (-1.8,0,1.8):g.fin(p+rot@np.array((xx,.25,.1)),.7,1.1,(0,yaw,0))
        else:
            g.lathe('ivory',(0,0,0),[(2.6,6.3),(2.6,7.25),(2.25,7.45),(2.25,8.25)],12)
            g.lathe('brass',(0,0,0),[(2.30,8.28),(2.52,8.42),(2.52,8.59)],12)
            g.lathe('slate',(0,0,0),[(2.48,8.61),(.5,10.6),(.15,10.9)],12)
            g.gem('energy',(0,11.08,0),.22,.65)
            for yaw in (0,90,180,270):g.window(rotation((0,yaw,0))@np.array((0,7.5,2.29)),.7,.72,(0,yaw,0))

def rampart(module):
    g=group(module,'rampart-base')
    g.box('stone',(0,.32,0),(15.2,.6,2.15),.15)
    g.box('ivory',(0,1.75,0),(14.8,2.45,1.55),.13)
    g.box('alloy',(0,3.05,0),(15.25,.28,2.05),.08)
    for x in np.arange(-6.8,7,1.95):
        g.box('ivory',(x,3.6,0),(1.12,.85,2.1),.11)
        g.box('stone',(x,4.04,0),(1.26,.13,2.2),.045)
    for x in (-6.6,-3.3,0,3.3,6.6):
        g.box('stone',(x,1.7,-.93),(.33,2.7,.44),.08)
        g.box('alloy',(x,1.8,.83),(.13,.7,.07),.025)
    # The corner opening reads as a deliberate entrance, not an unfinished wall.
    g.arch('stone',(9.55,0,-2.25),5.8,4.1,.29,.45,(0,45,0))
    for rank in range(1,4):
        g=group(module,'battlements-tier-'+str(rank),condition('Ramparts',rank))
        if rank==1:
            for x in (-5.7,0,5.7):
                g.box('ivory',(x,1.5,-1.03),(.85,2.7,.8),.18)
                g.box('brass',(x,2.8,-1.47),(.76,.15,.10),.035)
        elif rank==2:
            for x in (-5.8,-2.9,0,2.9,5.8):
                g.box('alloy',(x,1.75,.86),(.82,1.4,.10),.08)
                g.box('slate',(x,1.78,.94),(.58,1.15,.06),.05)
                g.gem('brass',(x,1.9,1.0),.20,.47,(90,0,0),4)
            g.box('brass',(0,2.95,1.08),(14.1,.10,.08),.02)
        else:
            for x in (-7.15,7.15):
                g.lathe('stone',(x,0,0),[(.7,.45),(.62,.9),(.5,4.15),(.72,4.3),(.72,4.6)],12)
                g.gem('slate',(x,5.03,0),.8,.9,sides=8)
                g.gem('energy',(x,5.57,0),.13,.35)
            g.arch('brass',(9.55,0,-2.25),5.8,4.1,.08,.53,(0,45,0))

def gun(module,artillery=False):
    rank_name='Artillery' if artillery else 'Ballista'
    g=group(module,'weapon-plinth')
    g.lathe('stone',(0,0,0),[(2.15,.0),(2.15,.4),(1.9,.55),(1.75,3.05),(2.15,3.25),(2.15,3.55)],32)
    g.lathe('alloy',(0,0,0),[(2.17,3.53),(2.17,3.72),(1.5,3.79)],32)
    for angle in range(0,360,60):
        rot=rotation((0,angle,0));g.window(rot@np.array((0,1.9,1.9)),.45,1.2,(0,angle,0))
        g.box('ivory',rot@np.array((0,1.72,1.72)),(.28,2.7,.56),.08,(0,angle,0))
    h=group(module,'weapon-head-frame',attachment=1)
    h.box('alloy',(0,.38,0),(2.2,.8,2.1),.25)
    for side in (-1,1):
        h.box('ivory',(side*.94,.56,-.15),(.55,.9,1.7),.16)
        h.box('slate',(side*1.15,.62,-.1),(.10,.51,1.15),.025)
        h.box('energy',(side*1.22,.62,.25),(.04,.13,.57),.01)
    if artillery:
        h.lathe('alloy',(0,.65,.3),[(.52,-.3),(.59,0),(.49,1.6),(.66,1.77),(.66,1.98),(.37,2.0),(.37,1.72)],36,(45,0,0),caps=False)
        h.lathe('glass',(0,.65,.3),[(.36,1.70),(.36,1.72)],36,(45,0,0))
    else:
        for count,limits in [(1,(1,2)),(2,(3,99))]:
            barrel=group(module,'repeater-barrels-'+str(count),condition('Ballista',*limits),attachment=1)
            for x in [0] if count==1 else [-.36,.36]:
                barrel.lathe('alloy',(x,.70,.25),[(.21,0),(.24,.18),(.19,1.55),(.28,1.66),(.28,1.86),(.15,1.9),(.15,1.72)],24,(90,0,0),caps=False)
                barrel.lathe('glass',(x,.70,.25),[(.14,1.70),(.14,1.72)],24,(90,0,0))
                barrel.ring('brass',(x,.70,1.50),.225,.035,(90,0,0),32)
    for rank in (2,3):
        g=group(module,rank_name.lower()+'-tier-'+str(rank),condition(rank_name,rank))
        if rank==2:
            g.lathe('ivory',(0,0,0),[(1.91,1.1),(2.04,1.18),(2.04,1.47),(1.91,1.54)],32)
            for a in range(0,360,90):g.fin(rotation((0,a,0))@np.array((0,2.2,1.95)),.55,.85,(0,a,0))
        else:
            for a in range(0,360,60):g.box('brass',rotation((0,a,0))@np.array((0,.8,1.93)),(.32,.82,.23),.06,(0,a,0))
        h=group(module,rank_name.lower()+'-head-tier-'+str(rank),condition(rank_name,rank),attachment=1)
        for side in (-1,1):
            h.box('brass',(side*(1.11 if rank==2 else .65),1.03,-.18),(.15,.19,1.6 if rank==2 else 1.9),.04)
            if rank==3:h.fin((side*1.1,.6,-.65),.7,.9,(0,side*20,0))
        if artillery:
            h.ring('energy',(0,1.35,1.0),.54,.05,(45,0,0),36)
            if rank==3:h.box('ivory',(0,.2,-1.18),(1.65,.58,.6),.17)

def frost():
    g=group(8,'frost-beacon')
    g.lathe('stone',(0,0,0),[(2.1,0),(2.1,.42),(1.6,.62),(1.25,3.2),(1.8,3.45),(1.8,3.65)],24)
    g.gem('energy',(0,4.7,0),.7,2.5,sides=8)
    for a in range(0,360,90):
        p=rotation((0,a,0))@np.array((0,3.1,1.2));g.fin(p,.8,1.55,(0,a,0))
    for rank in (2,3):
        g=group(8,'frost-tier-'+str(rank),condition('Frost',rank))
        g.ring('brass',(0,3.45+rank*.50,0),1.1+rank*.20,.08,sides=48)
        for a in range(0,360,90):
            p=rotation((0,a+rank*30,0))@np.array((0,3.9+rank*.23,1.4))
            g.gem('energy',p,.2,.8)
            g.box('alloy',p-(0,.65,0),(.18,.8,.18),.045)
        if rank==3:g.gem('brass',(0,6.35,0),.25,1.3,sides=6)

def hall(module,name):
    g=group(module,name+'-hall')
    g.box('stone',(0,.14,0),(7.4,.36,5.6),.13)
    g.box('alloy',(0,.42,0),(7,.23,5.2),.08)
    g.box('ivory',(0,1.9,0),(6.75,2.75,4.8),.18)
    g.box('stone',(0,3.36,0),(7.15,.32,5.2),.10)
    g.roof((0,3.55,0),7.6,5.65,1.4)
    for side in (-1,1):
        g.box('stone',(side*3.17,1.9,2.45),(.40,2.85,.42),.09)
        g.window((side*2.25,2.05,2.46),.65,1.15)
        g.box('stone',(side*3.17,1.9,-2.45),(.40,2.85,.42),.09)
        g.window((side*2.25,2.05,-2.46),.65,1.15,(0,180,0))
    g.box('stone',(0,1.9,-2.46),(.25,2.75,.24),.06)
    g.box('alloy',(0,1.55,2.45),(1.92,2.65,.16),.13)
    g.arch('brass',(0,.45,2.62),1.76,2.70,.12,.16)
    for x in (-.41,.41):g.box('slate',(x,1.63,2.56),(.76,2.14,.05),.045)
    g.box('energy',(0,2.24,2.65),(.075,.65,.035),.01)
    for x in (-3.44,3.44):
        for z in (-1.38,1.38):g.window((x,2,z),.75,1.35,(0,90 if x>0 else -90,0))
    # Separate, shallow approach steps leave the native stations accessible.
    for i in range(3):g.box('stone',(0,.08+i*.055,3.05-i*.23),(3.6,.12,1.05),.05)

def utilities():
    for module,name in [(9,'market'),(10,'forge'),(11,'workshop'),(12,'sanctum'),(13,'hangar'),(14,'archive'),(15,'kitchen')]:hall(module,name)
    for rank in (2,3):
        g=group(9,'market-tier-'+str(rank),condition('Market',rank))
        if rank==2:
            for x in (-4,4):
                g.box('ivory',(x,1.4,.1),(1.6,2.5,3.9),.15)
                g.roof((x,2.85,.1),1.9,4.3,.65)
                g.window((x,1.65,2.12),.72,.9)
        else:
            g.box('stone',(0,4.95,0),(2.8,.28,2.6),.1)
            g.roof((0,5.14,0),3.1,2.9,.95)
            for x in (-2.65,2.65):
                g.box('brass',(x,3.75,2.5),(.1,1.5,.1),.025)
                g.box('slate',(x,3.13,2.55),(.65,.9,.06),.035)
    # Each one-rank utility gets an authored architectural feature in addition
    # to its native station. None is represented solely by an icon change.
    accents={
        'Workshop':(11,'alloy'),'WhitePrinter':(11,'ivory'),'GreenPrinter':(11,'green'),'RedPrinter':(11,'red'),
        'Forge':(10,'brass'),'Reforge':(10,'energy'),'Sanctum':(12,'energy'),'Cleansing':(12,'glass'),
        'Order':(12,'brass'),'Recycler':(9,'energy'),'Hangar':(13,'ivory'),'DroneUpgrade':(13,'brass'),
        'DroneCombiner':(13,'energy'),'DroneScrapper':(13,'stone'),'Archive':(14,'energy'),
        'ArchiveGreen':(14,'green'),'Kitchen':(15,'brass'),'Donation':(12,'energy'),'GoldenVault':(9,'brass')}
    for name,(module,mat) in accents.items():
        g=group(module,name.lower()+'-architecture',condition(name))
        if name in ('WhitePrinter','GreenPrinter','RedPrinter'):
            slot=['WhitePrinter','GreenPrinter','RedPrinter'].index(name);x=(slot-1)*2
            g.box('alloy',(x,4.85,-.4),(1.05,.7,1.4),.16)
            g.box(mat,(x,4.87,.33),(.8,.39,.075),.035)
            for i in range(4):g.box('stone',(x-.32+i*.21,5.3,-.4),(.08,.20,1.0),.02)
        elif name in ('Forge','Kitchen'):
            for x in (-2.2,2.2):
                g.box('stone',(x,4.82,-.8),(.95,2.5,.95),.12)
                g.box('alloy',(x,6.1,-.8),(1.16,.25,1.16),.06)
                g.box('brass',(x,4.8,-.30),(.65,.18,.06),.025)
            if name=='Kitchen':g.box('brass',(0,3.65,2.65),(1.6,.28,.11),.04)
        elif name=='Reforge':
            for x in (-4.05,4.05):
                g.lathe('stone',(x,0,-.8),[(.8,.2),(.65,.6),(.65,2.8),(.9,3.0),(.9,3.2)],24)
                g.ring('brass',(x,3.35,-.8),.7,.12,sides=32)
        elif name=='Sanctum':
            g.lathe('ivory',(0,0,-.2),[(1.5,4.3),(1.45,5.55),(1.6,5.7)],24)
            g.lathe('slate',(0,0,-.2),[(1.6,5.73),(1.35,6.15),(.2,6.85)],24)
            g.gem('energy',(0,7.18,-.2),.25,.75)
        elif name=='Cleansing':
            for x in (-3.5,3.5):
                g.box('ivory',(x,2.1,-.7),(.25,3.5,3.6),.06)
                g.box('energy',(x,2.1,1.13),(.13,2.8,.05),.01)
        elif name=='Order':
            g.ring('brass',(0,7.3,-.2),.9,.075,(75,0,0),48)
            g.ring('brass',(0,7.3,-.2),.7,.06,(0,0,60),48)
            g.gem('energy',(0,7.3,-.2),.22,.75)
        elif name=='Donation':
            g.box('stone',(2.8,4.6,-1.7),(.65,1.6,.65),.09)
            g.gem('energy',(2.8,5.62,-1.7),.35,.95)
            g.ring('brass',(2.8,5.3,-1.7),.55,.07,sides=32)
        elif name=='Recycler':
            g.lathe('alloy',(-2.35,0,-1.2),[(.65,4.35),(.65,5.25),(.8,5.35)],24)
            for a in range(0,360,120):g.fin(np.array((-2.35,5.3,-1.2))+rotation((0,a,0))@np.array((0,0,.4)),.55,.4,(0,a,0),mat='slate')
        elif name=='GoldenVault':
            g.box('brass',(0,1.75,-2.5),(2.8,2.6,.26),.17)
            g.box('alloy',(0,1.75,-2.66),(2.25,2.16,.09),.12)
            g.ring('brass',(0,1.75,-2.75),.55,.11,(90,0,0),36)
            for x in (-1.45,1.45):g.fin((x,3.55,-1.7),.5,1,(0,0,0))
        elif name=='Hangar':
            g.arch('ivory',(0,.3,2.78),3.7,3.4,.19,.35)
            g.box('alloy',(0,.25,3.8),(4.6,.2,2.4),.1)
            for x in (-1.8,1.8):g.box('energy',(x,.37,3.85),(.08,.05,1.7),.015)
        elif name=='DroneUpgrade':
            g.box('alloy',(-2.6,5.3,-.8),(.45,4.8,.45),.1)
            g.box('brass',(-1.1,7.45,-.8),(3.7,.38,.45),.08)
            g.box('alloy',(.5,6.8,-.8),(.12,1.1,.12),.025)
            g.ring('brass',(.5,6.15,-.8),.24,.06,(90,0,0),24)
        elif name=='DroneCombiner':
            for x in (-2.3,2.3):
                g.lathe('alloy',(x,0,.3),[(.38,4.8),(.28,6),(.45,6.1),(.45,6.3)],20)
                g.gem('energy',(x,6.7,.3),.28,.9)
            g.box('brass',(0,6.25,.3),(4.9,.13,.16),.035)
        elif name=='DroneScrapper':
            g.box('stone',(3.55,1.55,-1.0),(1.0,2.65,2.2),.16)
            for y in (1.1,1.5,1.9,2.3):g.box('alloy',(4.08,y,-1.0),(.07,.12,1.8),.025)
        elif name in ('Archive','ArchiveGreen'):
            if name=='Archive':
                g.lathe('stone',(0,0,-.2),[(1.25,4.8),(1.0,5.2),(1.0,6),(1.25,6.15)],12)
                g.gem('energy',(0,6.9,-.2),.8,1.65,sides=8)
            else:
                for x in (-1.8,1.8):
                    g.box('brass',(x,5.18,-.2),(.15,1.35,.15),.04)
                    g.gem('green',(x,6,-.2),.27,.7)
                g.ring('brass',(0,6.3,-.2),1.75,.08,sides=48)
        elif name=='Workshop':
            g.box('alloy',(0,1.2,-2.7),(4.4,1.1,.55),.10)
            for x in (-1.5,-.5,.5,1.5):g.box('brass',(x,1.7,-3.02),(.2,.3,.12),.03)

def reinforcement():
    for module in range(16):
        for rank in (1,2,3):
            g=group(module,'ironclad-'+str(rank),condition('Reinforcement',rank))
            if module==0:
                for yaw in (0,90,180,270):
                    p=rotation((0,yaw,0))@np.array((0,1+rank*.42,3.52))
                    g.box('brass',p,(5.6,.12,.13),.035,(0,yaw,0))
            elif module<=4:
                for x in (-6.6,-3.3,0,3.3,6.6):
                    g.box('alloy',(x,.7+rank*.57,.93),(.47,.40,.14),.07)
                    g.gem('brass',(x,.7+rank*.57,1.04),.1,.23,(90,0,0),4)
            elif module<=8:
                g.lathe('brass',(0,0,0),[(1.86,.45+rank*.58),(1.96,.50+rank*.58),(1.96,.63+rank*.58),(1.86,.68+rank*.58)],32)
            else:
                for x in (-3.43,3.43):
                    g.box('alloy',(x,.55+rank*.7,0),(.18,.17,4.7),.04)
                    for z in (-1.7,1.7):g.box('brass',(x,.55+rank*.7,z),(.23,.32,.38),.045)

def ruins():
    for module in range(16):
        g=group(module,'ruined-foundation',ruin=True)
        wall=module in (1,2,3,4)
        g.box('stone',(0,.15,0),(14.5 if wall else 6.7,.36,1.9 if wall else 4.5),.15)
        for i in range(9):
            x=(i%3-1)*(4.1 if wall else 1.8);z=(i//3-1)*(.3 if wall else 1.1)
            g.box('stone',(x,.45+(i%3)*.16,z),(1.3,.75,1.0),.17,(i*17,i*31,i*11))
        for x in (-2.3,2.3):g.box('alloy',(x,.9,0),(.36,1.7,.48),.08,(0,0,x*9))

def textures():
    rng=np.random.default_rng(42019);size=1024
    yy,xx=np.mgrid[:size,:size]
    for name,(color,smooth,glow) in SURFACES.items():
        fine=rng.normal(0,.004,(size,size));value=np.full((size,size),.98)
        if name in ('ivory','stone'):
            row=yy//128;bx=(xx+(row%2)*128)%256;by=yy%128
            joint=(bx<5)|(by<5);edge=(bx<11)|(by<11)
            value-=joint*.23+edge*.045
            value+=.018*np.sin((xx+row*53)*.019)*np.sin(yy*.013)
        elif name in ('alloy','brass'):
            value+=.012*np.sin(yy*1.7)+.006*np.cos(xx*.02)
            value-=((xx%512<3)|(yy%512<3))*.12
        elif name=='slate':
            row=yy//128;bx=(xx+(row%2)*64)%128;by=yy%128
            value-=((bx<4)|(by<4))*.20
            value+=.018*np.sin((xx+yy)*.035)
        elif name=='glass':value=.85+.14*np.sin(xx/size*math.pi)
        rgb=np.uint8(np.clip((value+fine)*255,0,255));rgba=np.empty((size,size,4),np.uint8);rgba[:,:,:3]=rgb[:,:,None]
        rgba[:,:,3]=int(255*(.36 if name=='brass' else .19 if name in ('alloy','glass') else .08))
        Image.fromarray(rgba).save(OUT/f'castle_{name}.png',optimize=True)

def string(stream,value):
    b=value.encode();length=len(b)
    while length>=128:stream.write(bytes([(length&127)|128]));length>>=7
    stream.write(bytes([length]));stream.write(b)

def write():
    totals={};mat_names=list(SURFACES)
    with (OUT/'castle.meshbin').open('wb') as f:
        f.write(b'CAST1');f.write(struct.pack('<i',len(SURFACES)))
        for name,(color,smooth,glow) in SURFACES.items():string(f,name);f.write(struct.pack('<5f',*color,smooth,glow))
        f.write(struct.pack('<i',len(PARTS)))
        for (module,label,conditions,attachment,ruin,material),p in PARTS.items():
            string(f,label+'-'+material);f.write(struct.pack('<iii?',module,attachment,mat_names.index(material),ruin))
            f.write(struct.pack('<i',len(conditions)))
            for condition_value in conditions:f.write(struct.pack('<iii',*condition_value))
            f.write(struct.pack('<ii',len(p['v']),len(p['t'])*3))
            f.write(np.asarray(p['v'],dtype='<f4').tobytes());f.write(np.asarray(p['t'],dtype='<i4').tobytes())
            totals[label]=totals.get(label,0)+len(p['t'])
    report={'format':'CAST1','authoring':'scripts/build_castle_models.py','originalGeometry':True,'detailLevels':1,
            'parts':len(PARTS),'vertices':sum(len(p['v']) for p in PARTS.values()),'trianglesAcrossAllTiersAndRuins':sum(len(p['t']) for p in PARTS.values()),
            'surfaceResolution':1024,'materials':mat_names,'upgrades':dict(zip(NAMES,MAXIMA)),'trianglesByLayer':totals}
    (OUT/'castle-info.json').write_text(json.dumps(report,indent=2))
    print(json.dumps({k:v for k,v in report.items() if k!='trianglesByLayer'},indent=2))

if __name__=='__main__':
    keep()
    for m in (1,2,3,4):rampart(m)
    for m in (5,6):gun(m)
    gun(7,True);frost();utilities();reinforcement();ruins();write();textures()
