"""Original optional Hellenic decoration and flush themed-floor geometry."""
import math
import numpy as np
from build_castle_models import Geometry, PARTS, rotation, unit


def polar(radius, y, degrees):
    a=math.radians(degrees)
    return np.array((radius*math.sin(a), y, radius*math.cos(a)))


class Placed:
    def __init__(self, geometry, origin, yaw=0):
        self.g=geometry;self.origin=np.array(origin,float);self.yaw=yaw;self.rot=rotation((0,yaw,0))
    def point(self,p):return self.origin+self.rot@np.array(p,float)
    def face(self,material,points,want=None):self.g.face(material,points,want,(self.origin,self.rot))
    def box(self,material,center,size,bevel=.05,angles=(0,0,0)):
        self.g.box(material,self.point(center),size,bevel,(angles[0],angles[1]+self.yaw,angles[2]))
    def lathe(self,material,center,profile,sides=40,angles=(0,0,0),caps=True):
        self.g.lathe(material,self.point(center),profile,sides,(angles[0],angles[1]+self.yaw,angles[2]),caps)
    def ring(self,material,center,radius,tube=.06,angles=(0,0,0),sides=48):
        self.g.ring(material,self.point(center),radius,tube,(angles[0],angles[1]+self.yaw,angles[2]),sides)


def smooth_mesh(p,material,positions,normals,uv,triangles):
    positions=np.asarray(positions)@p.rot.T+p.origin
    normals=np.asarray(normals)@p.rot.T
    key=(p.g.module,p.g.label,p.g.conditions,p.g.attachment,p.g.ruin,material)
    part=PARTS.setdefault(key,{'v':[],'t':[]});offset=len(part['v'])
    part['v'].extend(np.column_stack((positions,normals,uv)).tolist())
    part['t'].extend((np.array(triangles,int)+offset).tolist())


def ellipsoid(p,material,center,scale,rows=16,sides=36,basis=None):
    center=np.array(center);scale=np.array(scale);basis=np.eye(3) if basis is None else basis
    vertices=[];normals=[];uv=[];triangles=[]
    for row in range(rows+1):
        a=-math.pi*.5+row/rows*math.pi
        for side in range(sides+1):
            b=side/sides*math.tau
            sphere=np.array((math.cos(a)*math.cos(b),math.sin(a),math.cos(a)*math.sin(b)))
            vertices.append(center+basis@(sphere*scale));normals.append(basis@unit(sphere/scale))
            uv.append((side/sides,row/rows))
    vertex_array=np.asarray(vertices);normal_array=np.asarray(normals)
    for row in range(rows):
        for side in range(sides):
            a=row*(sides+1)+side;b=a+sides+1
            for tri in ((a,b,a+1),(a+1,b,b+1)):
                if row==0 and tri[0]==a:continue
                if row==rows-1 and tri[0]==a+1:continue
                pts=vertex_array[list(tri)]
                if np.dot(np.cross(pts[1]-pts[0],pts[2]-pts[0]),np.mean(normal_array[list(tri)],axis=0))<0:tri=tri[::-1]
                triangles.append(tri)
    smooth_mesh(p,material,vertices,normals,uv,triangles)


def limb(p,material,a,b,radius,flatten=1):
    a,b=np.array(a,float),np.array(b,float);direction=unit(b-a)
    x=unit(np.cross(direction,(0,0,1)))
    if np.linalg.norm(x)<.1:x=unit(np.cross(direction,(1,0,0)))
    z=unit(np.cross(x,direction));basis=np.column_stack((x,direction,z))
    ellipsoid(p,material,(a+b)*.5,(radius,np.linalg.norm(b-a)*.5+radius*.24,radius*flatten),10,24,basis)


def plinth(p,size=1):
    for center,dimensions,material,bevel in [
        ((0,.18,0),(5.1,.36,3.0),'basalt',.08),((0,.45,0),(4.75,.24,2.85),'marble',.055),
        ((0,.79,0),(4.35,.46,2.55),'limestone',.07),((0,1.10,0),(4.65,.18,2.75),'marble',.04)]:
        p.box(material,np.array(center)*size,np.array(dimensions)*size,bevel*size)
    for x in np.linspace(-1.78,1.78,9):
        p.box('accent',(x*size,.81*size,1.285*size),(.09*size,.26*size,.035*size),.009*size)


def relief_leaf(p,center,direction,length,width,material='accent'):
    center=np.array(center,float);direction=unit(direction)
    across=unit(np.cross((0,0,1),direction))
    outline=[]
    for t in np.linspace(0,1,7):outline.append(center+direction*length*t+across*width*math.sin(math.pi*t))
    for t in np.linspace(1,0,7)[1:]:outline.append(center+direction*length*t-across*width*math.sin(math.pi*t))
    ridge=center+direction*length*.47+np.array((0,0,.085))
    for a,b in zip(outline,outline[1:]+outline[:1]):p.face(material,(a,b,ridge),(0,0,1))


def wreath(p,center,radius,material='accent',leaves=26):
    center=np.array(center,float)
    p.ring(material,center,radius,.055,(90,0,0),64)
    for side in (-1,1):
        for angle in np.linspace(-72,78,leaves//2):
            a=math.radians(angle)
            position=center+np.array((side*radius*math.cos(a),radius*math.sin(a),.02))
            direction=unit((side*.72, .70, 0))
            relief_leaf(p,position,direction,radius*.29,radius*.075,material)


def guardian(p):
    plinth(p)
    for sign, forward in ((-1,.05),(1,.42)):
        foot=(sign*.63,1.37,forward+.15)
        ellipsoid(p,'marble',foot,(.38,.22,.72),12,32)
        knee=(sign*.58,2.83,forward-.03);hip=(sign*.47,4.05,-.05)
        limb(p,'marble',(sign*.63,1.55,forward-.09),knee,.31)
        limb(p,'marble',knee,hip,.43)
        p.box('accent',(sign*.62,2.05,forward+.19),(.37,.93,.12),.06)
    ellipsoid(p,'marble',(0,4.13,-.03),(.83,.55,.58),16,36)
    ellipsoid(p,'marble',(0,5.15,-.05),(1.03,.92,.60),22,44)
    p.lathe('accent',(0,4.43,-.02),[(.76,0),(.79,.06),(.79,.22),(.76,.27)],48)
    # Articulated-looking pteruges, each a real beveled stone armor tab.
    for j in range(18):
        a=j*math.tau/18
        p.box('marble',(.78*math.cos(a),3.85,.54*math.sin(a)),(.21,.76,.13),.035,(0,-math.degrees(a),0))
    ellipsoid(p,'marble',(0,6.13,-.02),(.29,.39,.28),12,28)
    ellipsoid(p,'marble',(0,6.86,0),(.53,.69,.49),22,44)
    ellipsoid(p,'marble',(0,7.13,-.07),(.61,.47,.57),20,48)
    p.box('basalt',(0,6.98,.50),(.91,.13,.09),.035)
    p.box('marble',(0,6.80,.59),(.15,.49,.19),.035)
    for side in (-1,1):
        p.box('marble',(side*.44,6.62,.43),(.25,.62,.20),.045,(0,side*-12,side*-9))
        p.ring('accent',(side*.52,6.98,-.025),.18,.035,(0,0,90),32)
    # Carved Corinthian helmet crest, continuous silhouette with relief ribs.
    crest=[]
    for z in np.linspace(-.55,.55,25):
        arch=math.sqrt(max(0,1-(z/.72)**2))
        crest.append([(-.105,7.40+.13*arch,z),(.105,7.40+.13*arch,z),
                      (.105,7.68+.44*arch,z),(-.105,7.68+.44*arch,z)])
    for a,b in zip(crest,crest[1:]):
        for side in range(4):p.face('accent',[a[side],b[side],b[(side+1)%4],a[(side+1)%4]])
    p.face('accent',crest[0]);p.face('accent',crest[-1][::-1])
    for z in np.linspace(-.50,.50,19):
        arch=math.sqrt(max(0,1-(z/.72)**2))
        p.box('marble',(0,7.57+.28*arch,z),(.225,.21+.22*arch,.018),.007)
    for side in (-1,1):
        shoulder=(side*1.01,5.68,-.03)
        elbow=(side*1.43,4.92,.18)
        hand=(-1.40,4.55,1.02) if side<0 else (1.57,5.57,.47)
        limb(p,'marble',shoulder,elbow,.34)
        limb(p,'marble',elbow,hand,.28)
        ellipsoid(p,'marble',hand,(.24,.28,.25),12,24)
        ellipsoid(p,'marble',(side*1.03,5.77,0),(.46,.33,.49),14,32)
    shield=(-1.42,4.57,1.07)
    p.lathe('marble',shield,[(.07,-.14),(1.10,-.16),(1.32,-.04),(1.37,.08),
        (1.32,.18),(.9,.25),(.08,.30)],64,(90,0,0))
    p.ring('accent',(-1.42,4.57,1.28),1.28,.065,(90,0,0),64)
    ellipsoid(p,'accent',(-1.42,4.57,1.40),(.31,.31,.13),16,32)
    wreath(p,(-1.42,4.57,1.325),.79,leaves=20)
    for j in range(12):
        a=j*math.tau/12
        ellipsoid(p,'accent',(-1.42+1.16*math.cos(a),4.57+1.16*math.sin(a),1.30),(.045,.045,.035),8,12)
    # Spear is wholly on the parapet side of the fighting floor.
    p.lathe('accent',(1.57,1.2,.47),[(.075,0),(.075,7.8),(.11,7.85)],24)
    p.lathe('marble',(1.57,9.0,.47),[(.02,0),(.24,.32),(.16,.70),(.002,1.15)],24)
    # Heavy drapery behind the stone figure, with actual folds, not painted stripes.
    for row in range(24):
        for col in range(20):
            points=[]
            for u,t in ((col/20,row/24),(col/20,(row+1)/24),((col+1)/20,(row+1)/24),((col+1)/20,row/24)):
                x=(u*2-1)*(1.15+t*.38)
                points.append((x,5.85-t*4.38,-.48-t*.30-.17*math.sin(u*math.pi*10)*math.sin(math.pi*t)))
            p.face('limestone',points,(0,0,-1))
            p.face('limestone',points[::-1],(0,0,1))


def banner(p):
    p.lathe('basalt',(-2.1,0,0),[(.65,0),(.70,.18),(.49,.43),(.32,.60)],40)
    p.lathe('accent',(-2.1,.52,0),[(.16,0),(.16,9.75),(.23,9.82)],32)
    for y in (1.0,3.3,6.0,9.0):p.lathe('accent',(-2.1,y,0),[(.18,0),(.22,.08),(.18,.16)],32)
    p.box('accent',(0,9.75,0),(4.65,.20,.22),.045)
    p.ring('accent',(-2.1,10.7,0),.39,.065,(90,0,0),48)
    for side in (-1,1):
        p.box('accent',(side*2.18,9.75,0),(.18,.44,.28),.04)
    positions=[];uv=[]
    across=20;down=32
    for row in range(down+1):
        t=row/down
        for col in range(across+1):
            u=col/across
            y=9.42-t*6.35+.75*(1-abs(u*2-1))*t**8
            z=.26*math.sin(t*math.pi)*math.sin(u*math.pi*5+t*3)+.11*math.sin(t*math.pi*3)*u
            positions.append((u*3.8-1.9,y,z));uv.append((u,1-t))
    positions=np.asarray(positions).reshape(down+1,across+1,3)
    dt,du=np.gradient(positions,axis=(0,1))
    normals=np.cross(dt,du);normals/=np.linalg.norm(normals,axis=2,keepdims=True)
    positions=positions.reshape(-1,3);normals=normals.reshape(-1,3);triangles=[]
    for row in range(down):
        for col in range(across):
            a=row*(across+1)+col;b=a+across+1
            triangles.extend([(a,b,b+1),(a,b+1,a+1)])
    smooth_mesh(p,'cloth',positions,normals,uv,triangles)
    smooth_mesh(p,'cloth',positions,-normals,uv,[tri[::-1] for tri in triangles])


def brazier(p):
    p.lathe('basalt',(0,0,0),[(1.35,0),(1.44,.20),(1.23,.42),(.89,.62),(.72,.87),(.72,2.45),(.97,2.67)],48)
    for j in range(16):
        a=j*math.tau/16
        p.box('marble',(.72*math.sin(a),1.65,.72*math.cos(a)),(.12,1.48,.09),.018,(0,math.degrees(a),0))
    p.lathe('accent',(0,2.58,0),[(.91,0),(1.10,.18),(1.52,.62),(1.63,.92),(1.63,1.06),
        (1.43,1.13),(1.38,.94),(.89,.40),(.32,.27)],64)
    p.ring('accent',(0,3.64,0),1.55,.10,(0,0,0),64)
    for side in (-1,1):p.ring('accent',(side*1.64,3.13,0),.39,.075,(90,0,0),40)
    for j in range(24):
        a=j*2.399963;r=.28+1.0*math.sqrt((j+.5)/24)
        p.lathe('basalt',(r*math.sin(a),3.14+(j%3)*.035,r*math.cos(a)),[(.09,0),(.22,.08),(.17,.21),(.04,.26)],8)
        if j%3==0:ellipsoid(p,'ember',(r*math.sin(a),3.35,r*math.cos(a)),(.07,.035,.06),6,10)


def amphora(p,scale=1):
    profile=[(.35,0),(.48,.12),(.32,.28),(.37,.43),(.65,.85),(.70,1.15),(.53,1.47),(.28,1.65),(.27,1.94),(.42,2.04),(.43,2.16),(.29,2.20)]
    p.lathe('accent',(0,0,0),[(r*scale,y*scale) for r,y in profile],48)
    for side in (-1,1):p.ring('accent',(side*.57*scale,1.62*scale,0),.36*scale,.065*scale,(90,0,0),48)
    p.ring('marble',(0,1.08*scale,0),.705*scale,.035*scale,sides=64)
    p.lathe('basalt',(0,-.20*scale,0),[(.65*scale,0),(.65*scale,.15*scale),(.50*scale,.2*scale)],32)


def trophy(p):
    p.box('basalt',(0,.08,0),(1.65,.16,1.35),.06)
    p.box('marble',(0,.32,0),(1.35,.34,1.08),.06)
    p.box('accent',(0,.55,0),(1.45,.12,1.15),.035)
    for j in range(12):
        a=j*math.tau/12
        x,z=.43*math.cos(a),.43*math.sin(a)
        p.box('accent',(x,.72,z),(.22,.22,.07),.02,(0,-math.degrees(a),0))
        p.lathe('accent',(x,.76,z),[(.065,0),(.12,.13),(.002,.38)],12)
    p.ring('accent',(0,.65,0),.46,.06,sides=48)
    p.ring('accent',(0,.86,0),.46,.04,sides=48)


def build_decorations():
    sets=[]
    g=Geometry(0,'Decorsentinels')
    placements=[]
    for angle in (45,135,225,315):
        point=polar(181.9,2.85,angle);guardian(Placed(g,point,angle+180));placements.append(point.tolist())
    sets.append(dict(id='sentinels',name='Arena Guardians',groupPrefix='Decorsentinels',
        description='Four carved hoplites with Corinthian crests, shields, spears and heavy stone drapery.',
        placements=placements,collision=False,support='Inner parapet at y=2.85, away from gates and radial aisles.'))
    g=Geometry(0,'Decorstandards');placements=[]
    for angle in (15,75,105,155,205,255,285,345):
        point=polar(181.9,2.85,angle);banner(Placed(g,point,angle+180));placements.append(point.tolist())
    sets.append(dict(id='standards',name='Champion Standards',groupPrefix='Decorstandards',
        description='Eight tall bronze standards with double-sided folded cloth and theme-specific embroidery.',
        placements=placements,collision=False,support='Inner parapet; no text labels or runtime cloth simulation.'))
    g=Geometry(0,'Decorbraziers');placements=[];fire=[]
    for angle in (22.5,67.5,112.5,157.5,202.5,247.5,292.5,337.5):
        point=polar(181.9,2.85,angle);p=Placed(g,point,angle+180);brazier(p);placements.append(point.tolist());fire.append(p.point((0,3.6,0)).tolist())
    sets.append(dict(id='braziers',name='Victory Braziers',groupPrefix='Decorbraziers',
        description='Eight fluted stone pedestals, ribbed bronze bowls, handles and banked embers.',
        placements=placements,optionalFireVfxAnchors=fire,collision=False,
        support='Inner parapet; ornamental, separate from the interactive Ready brazier.'))
    g=Geometry(0,'Decortrophies');placements=[]
    for x in (-44,-22,0,22,44):
        point=np.array((x-2.5,1.90,-189));amphora(Placed(g,point),.65);placements.append(point.tolist())
        point=np.array((x+2.5,1.90,-189));trophy(Placed(g,point));placements.append(point.tolist())
    sets.append(dict(id='trophies',name='Victors Gallery',groupPrefix='Decortrophies',
        description='Five handled victory amphorae and five laurel crowns on the existing rear merchant shelves.',
        placements=placements,collision=False,support='Rear shelves at z=-189; no terminals, doors or shopping lanes occupied.'))
    g=Geometry(0,'Decorlaurels');placements=[]
    for angle in range(15,360,30):
        point=polar(229.03,32.00,angle);p=Placed(g,point,angle+180)
        p.lathe('basalt',(0,0,-.12),[(1.10,0),(1.24,.08),(1.24,.17),(1.08,.23)],48,(90,0,0))
        wreath(p,(0,0,.12),1.25,leaves=28)
        p.lathe('accent',(0,0,.18),[(.002,0),(.28,.07),(.24,.15),(.002,.22)],32,(90,0,0))
        placements.append(point.tolist())
    sets.append(dict(id='laurels',name='Laurels of Renown',groupPrefix='Decorlaurels',
        description='Twelve sculpted laurel-and-boss medallions attached to the inner peristyle frieze.',
        placements=placements,collision=False,support='Facade-mounted at y=32; columns and circulation remain clear.'))
    return sets


def ground(g,material,points,y=.052):
    clean=[]
    for point in points:
        if not clean or np.linalg.norm(np.array(point)-clean[-1])>1e-7:clean.append(np.array(point))
    if len(clean)>2 and np.linalg.norm(clean[0]-clean[-1])<1e-7:clean.pop()
    if len(clean)>=3:g.face(material,[(x,y,z) for x,z in clean],(0,1,0))


def arc(g,material,r0,r1,start=0,end=math.tau,y=.052,segments=None):
    segments=segments or max(1,int((end-start)/math.tau*384))
    for j in range(segments):
        a=start+(end-start)*j/segments;b=start+(end-start)*(j+1)/segments
        if r0==0:
            ground(g,material,[(0,0),(r1*math.sin(b),r1*math.cos(b)),(r1*math.sin(a),r1*math.cos(a))],y)
        else:
            ground(g,material,[(r0*math.sin(a),r0*math.cos(a)),(r0*math.sin(b),r0*math.cos(b)),
                               (r1*math.sin(b),r1*math.cos(b)),(r1*math.sin(a),r1*math.cos(a))],y)


def floor_leaf(g,center,direction,length,width,material,y=.060):
    center=np.array(center);direction=unit(direction);across=np.array((-direction[1],direction[0]))
    outline=[]
    for t in np.linspace(0,1,10):outline.append(center+direction*length*t+across*width*math.sin(math.pi*t))
    for t in np.linspace(1,0,10)[1:]:outline.append(center+direction*length*t-across*width*math.sin(math.pi*t))
    middle=center+direction*length*.48
    for a,b in zip(outline,outline[1:]+outline[:1]):ground(g,material,(middle,a,b),y)


def key_border(g,material,radius,count=128,y=.058):
    for j in range(count):
        a=j*math.tau/count;b=a+math.tau/count*.78
        for r0,r1,aa,bb in [(radius,radius+.36,a,b),(radius+2.2,radius+2.56,a,b),
                            (radius+.36,radius+2.2,a,a+.003),(radius+.36,radius+2.2,b-.003,b),
                            (radius+1.06,radius+1.42,a+.004,b-.004)]:arc(g,material,r0,r1,aa,bb,y,2)


def build_floor(theme):
    g=Geometry(0,'DecorFloor'+theme.title())
    for j in range(384):
        a=j*math.tau/384;b=(j+1)*math.tau/384
        ground(g,'paving',((0,0),(178.85*math.sin(a),178.85*math.cos(a)),(178.85*math.sin(b),178.85*math.cos(b))),.04)
    arc(g,'basalt',175.5,177.2,y=.049);arc(g,'marble',177.2,178.7,y=.051)
    if theme=='gilded':
        key_border(g,'accent',169.7)
        arc(g,'marble',48,49.3,y=.053);arc(g,'accent',51,51.65,y=.057)
        arc(g,'basalt',0,43,y=.048)
        for j in range(12):
            a=j*math.tau/12
            ground(g,'accent',((0,0),(11*math.sin(a-.15),11*math.cos(a-.15)),
                (39*math.sin(a),39*math.cos(a)),(11*math.sin(a+.15),11*math.cos(a+.15))),.059)
        for side in (-1,1):
            for degree in np.linspace(-73,78,22):
                a=math.radians(degree);position=(side*55*math.cos(a),55*math.sin(a))
                floor_leaf(g,position,(side*.72,.70),7.5,1.65,'accent')
        for j in range(12):
            a=j*math.tau/12+.045
            arc(g,'marble',83.2,84.0,a,a+.30,.052,12)
    elif theme=='lunar':
        key_border(g,'marble',169.7)
        arc(g,'basalt',0,46,y=.047);arc(g,'accent',46.8,47.3,y=.053)
        R,r,d=34,31,11
        x=(R*R-r*r+d*d)/(2*d);outer=math.acos(x/R);inner=math.acos((x-d)/r)
        for j in range(192):
            t0=j/192;t1=(j+1)/192
            points=[]
            for t,rad,offset,angle in ((t0,R,0,outer),(t1,R,0,outer),(t1,r,d,inner),(t0,r,d,inner)):
                a=angle+t*(math.tau-2*angle);points.append((offset+rad*math.cos(a),rad*math.sin(a)))
            ground(g,'marble',points,.060)
        for j in range(24):
            a=j*2.399963;r0=64+45*((j%5)/4)
            cx,cz=r0*math.sin(a),r0*math.cos(a);size=2.2+(j%3)*.8
            ground(g,'accent',[(cx,cz-size),(cx+size*.3,cz),(cx,cz+size),(cx-size*.3,cz)],.060)
        for radius in (112,148):arc(g,'marble',radius,radius+.34,y=.053)
    else:
        arc(g,'marble',44,46,y=.051)
        for j in range(12):
            a=j*math.tau/12+.08
            arc(g,'accent',47,48,a,a+.31,.056,12)
            for k in range(4):
                t=a+k*.07
                floor_leaf(g,(50*math.sin(t),50*math.cos(t)),(math.sin(t+.6),math.cos(t+.6)),6,1.25,'accent')
        for j in range(12):
            a=j*math.tau/12+.10;b=a+.31
            arc(g,'moss',125,167,a,b,.050,20)
            arc(g,'marble',124.3,125,a,b,.053,20)
        for j in range(96):
            if j%11 in (0,1):continue
            a=j*math.tau/96
            arc(g,'basalt',170,171.4,a,a+.045,.053,3)
    # Match the original courtyard stone scale; finer metallic inlays stay crisp.
    for key,part in PARTS.items():
        if key[1]!=g.label:continue
        scale=12 if key[-1] in ('paving','marble','basalt') else 5
        for vertex in part['v']:vertex[6]=vertex[0]/scale;vertex[7]=vertex[2]/scale
