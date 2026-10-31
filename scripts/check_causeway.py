"""Check authored causeway against installed navigation data, without running Unity."""
import json,math,re,runpy
from collections import deque
from pathlib import Path
root=Path(__file__).resolve().parent.parent
data=runpy.run_path(str(root/'scripts/check_castle_assets.py'))
ground,center=data['ground'],data['center']
source=(root/'src/FortressApproach.cs').read_text(encoding='utf-8')
declaration=source.split('Vector3[] Route=')[1].split(';',1)[0]
route=[tuple(float(value.strip().rstrip('f')) for value in values.split(',')) for values in re.findall(r'new Vector3\(([^)]+)\)',declaration)]
width=float(re.search(r'const float Width=([\d.]+)',source)[1])
assert width>=24 and len(route)>=3,'Main approach must remain broad and connected.'
points=[tuple(node['position'][axis] for axis in ('x','y','z')) for node in ground['nodes']]
old_count=len(points);links=[(link['nodeIndexA']['nodeIndex'],link['nodeIndexB']['nodeIndex'],link['hullMask'],link['jumpHullMask'],link['gateIndex']) for link in ground['links']]
previous=[];endpoints=[];slopes=[]
def both(a,b,mask):links.extend(((a,b,mask,0,0),(b,a,mask,0,0)))
for segment,(a,b) in enumerate(zip(route,route[1:])):
    delta=tuple(y-x for x,y in zip(a,b));flat=math.hypot(delta[0],delta[2]);distance=math.dist(a,b)
    slopes.append(math.degrees(math.atan2(abs(delta[1]),flat)))
    side=(delta[2]/flat,0,-delta[0]/flat);rows=math.ceil(distance/4)
    for row in range(rows+1):
        current=[]
        for lane in range(3):
            current.append(len(points));points.append(tuple(center[i]+a[i]+delta[i]*row/rows+side[i]*(lane-1)*5+(.06 if i==1 else 0) for i in range(3)))
            if segment==0 and row==0 or segment==len(route)-2 and row==rows:endpoints.append(current[-1])
            if lane>0:both(current[-2],current[-1],7)
        if previous:
            for lane in range(3):
                for other in range(3):
                    if abs(lane-other)<=1:both(previous[lane],current[other],7)
        previous=current
assert max(slopes)<20,'Main ascent became too steep for the intended grounded approach.'
for end in endpoints:
    for i,node in enumerate(ground['nodes']):
        if abs(points[end][1]-points[i][1])<=1.2 and math.dist(points[end],points[i])<=18 and node['gateIndex']==0:
            mask=7&~node['forbiddenHulls']
            if mask:both(end,i,mask)
results={}
for hull,name in ((1,'human'),(2,'golem'),(4,'beetleQueen')):
    active={i for i,node in enumerate(ground['nodes']) if not node['forbiddenHulls']&hull and node['gateIndex']==0}|set(range(old_count,len(points)))
    goal=min(active,key=lambda i:math.dist(points[i],center));incoming=[[] for _ in points]
    for a,b,mask,jump,gate in links:
        if mask&hull and not jump&hull and gate==0:incoming[b].append(a)
    reached={goal};queue=deque([goal])
    while queue:
        for a in incoming[queue.popleft()]:
            if a not in reached:reached.add(a);queue.append(a)
    assert all(i in reached for i in range(old_count,old_count+3)),f'Lower causeway landing cannot reach the keep for {name}.'
    candidates=[i for i in reached&active if 80<=math.dist(points[i],center)<=140 and points[i][1]<center[1]-8]
    assert candidates,f'No distant lower approach spawn candidates for {name}.'
    results[name]=len(candidates)
# Independent physical-clearance regression against the installed native ascent.
# Connectivity alone cannot detect a bridge slab hanging across a walking path.
import heapq
def road_triangles(path):
    def side(a,b):
        x,z=b[0]-a[0],b[2]-a[2];length=math.hypot(x,z);return (z/length,0,-x/length)
    def smooth(t):
        t=max(0,min(1,t));return t*t*(3-2*t)
    total=sum(math.dist(a,b) for a,b in zip(path,path[1:]));distance=0;sections=[]
    for segment,(a,b) in enumerate(zip(path,path[1:])):
        length=math.dist(a,b);rows=math.ceil(length/4)
        for row in range(0 if segment==0 else 1,rows+1):
            t=row/rows;along=distance+length*t;direction=side(a,b)
            if row==rows and segment<len(path)-2:
                nxt=side(b,path[segment+2]);v=tuple(x+y for x,y in zip(direction,nxt));vlen=math.sqrt(sum(x*x for x in v));v=tuple(x/vlen for x in v)
                dot=sum(x*y for x,y in zip(v,direction));direction=tuple(x/max(.6,dot) for x in v)
            center=tuple(x+(y-x)*t for x,y in zip(a,b));half=width/2+2*smooth(1-(total-along)/12)
            sections.append(tuple(tuple(center[j]+sign*direction[j]*half for j in range(3)) for sign in (-1,1)))
        distance+=length
    triangles=[]
    for (al,ar),(bl,br) in zip(sections,sections[1:]):triangles.extend(((al,ar,br),(al,br,bl)))
    return triangles,sections

def height_at(point,triangles):
    x,z=point[0],point[2];highest=None
    for a,b,c in triangles:
        if not min(a[0],b[0],c[0])-.001<=x<=max(a[0],b[0],c[0])+.001 or not min(a[2],b[2],c[2])-.001<=z<=max(a[2],b[2],c[2])+.001:continue
        det=(b[2]-c[2])*(a[0]-c[0])+(c[0]-b[0])*(a[2]-c[2])
        if abs(det)<1e-7:continue
        u=((b[2]-c[2])*(x-c[0])+(c[0]-b[0])*(z-c[2]))/det
        v=((c[2]-a[2])*(x-c[0])+(a[0]-c[0])*(z-c[2]))/det
        w=1-u-v
        if min(u,v,w)>=-.0001:
            y=u*a[1]+v*b[1]+w*c[1];highest=y if highest is None else max(highest,y)
    return highest

native_points=[tuple(n['position'][axis]-center[j] for j,axis in enumerate(('x','y','z'))) for n in ground['nodes']]
allowed={i for i,n in enumerate(ground['nodes']) if n['gateIndex']==0 and not n['forbiddenHulls']&2}
incoming=[[] for _ in native_points]
for link in ground['links']:
    a,b=link['nodeIndexA']['nodeIndex'],link['nodeIndexB']['nodeIndex']
    if a in allowed and b in allowed and link['gateIndex']==0 and link['hullMask']&2 and not link['jumpHullMask']&2:incoming[b].append(a)
goal=min(allowed,key=lambda i:math.dist(native_points[i],(0,0,0)));costs={goal:0};queue=[(0,goal)];nxt={}
while queue:
    cost,b=heapq.heappop(queue)
    if cost>costs[b]:continue
    for a in incoming[b]:
        value=cost+math.dist(native_points[a],native_points[b])
        if value<costs.get(a,math.inf):costs[a]=value;nxt[a]=b;heapq.heappush(queue,(value,a))
starts=[i for i in allowed if 70<math.dist(native_points[i],(0,0,0))<160 and native_points[i][1]<-8]
node=min(starts,key=lambda i:costs.get(i,math.inf));native_path=[]
while node in nxt:native_path.append(native_points[node]);node=nxt[node]
native_path.append(native_points[node]);assert len(native_path)>4
samples=[]
for a,b in zip(native_path,native_path[1:]):
    count=math.ceil(math.dist(a,b)/.5);dx,dz=b[0]-a[0],b[2]-a[2];length=math.hypot(dx,dz)
    for i in range(count+1):
        point=tuple(x+(y-x)*i/count for x,y in zip(a,b))
        for offset in (-2,0,2):samples.append((point[0]+dz/length*offset,point[1],point[2]-dx/length*offset))
deck,road_sections=road_triangles(route)
def obstruction(triangles):
    values=[max(0,y-point[1]) for point in samples if (y:=height_at(point,triangles)) is not None]
    return max(values,default=0)
max_obstruction=obstruction(deck)
baseline_deck,_=road_triangles([(82,-20,-70),(65,-20,-60),(0,0,-34),(-18,0,-26.8)])
baseline_obstruction=obstruction(baseline_deck)
assert baseline_obstruction>2,'Clearance regression must detect the shipped 0.7 entrance obstruction.'
assert max_obstruction<.35,f'Causeway blocks the original walking corridor by {max_obstruction:.2f}m.'
# Every top triangle must retain one winding; tight-offset folds create bad collision.
winding=[]
for a,b,c in deck:
    signed=(b[0]-a[0])*(c[2]-a[2])-(b[2]-a[2])*(c[0]-a[0]);assert abs(signed)>.001
    winding.append(signed>0)
assert all(winding) or not any(winding),'Causeway deck folds or crosses itself at a turn.'
print('PASS: native entrance clearance; old obstruction',round(baseline_obstruction,2),'m, new',round(max_obstruction,3),'m across',len(samples),'walking-corridor samples.')

receipt={'width':width,'maximumInclineDegrees':round(max(slopes),2),'addedNodes':len(points)-old_count,'distantLowerCandidates':results,
         'runtimeValidatesWorldClearanceAndRoutes':True,'originalEntranceMaximumObstructionMetres':round(max_obstruction,3),'previousObstructionDetectedMetres':round(baseline_obstruction,2),'livePhysicsTested':False,'gameLaunched':False}
(root/'.build/causeway-check.json').write_text(json.dumps(receipt,indent=2),encoding='utf-8')
print('PASS: authored causeway width/incline and directed approaches against installed navigation:',json.dumps(receipt))
