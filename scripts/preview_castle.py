"""Studio renders of the exact CAST1 meshes embedded in the playable mod."""
from pathlib import Path
import struct,math,json,sys
import numpy as np
from PIL import Image,ImageDraw,ImageFont,ImageFilter
from build_castle_models import NAMES,MAXIMA,SURFACES,rotation,unit

ROOT=Path(__file__).resolve().parent.parent
OUT=ROOT/'dist/castle-model-review'
OUT.mkdir(parents=True,exist_ok=True)

def read_asset():
    with (ROOT/'assets/castle.meshbin').open('rb') as f:
        def read(fmt):return struct.unpack('<'+fmt,f.read(struct.calcsize('<'+fmt)))
        def string():
            count=shift=0
            while True:
                byte=read('B')[0];count|=(byte&127)<<shift
                if byte<128:break
                shift+=7
            return f.read(count).decode()
        assert f.read(5)==b'CAST1'
        surfaces=[]
        for _ in range(read('i')[0]):surfaces.append((string(),read('5f')))
        parts=[]
        for _ in range(read('i')[0]):
            name=string();module,attachment,material,ruin=read('iii?')
            conditions=[read('iii') for _ in range(read('i')[0])]
            nv,ni=read('ii');vertices=np.frombuffer(f.read(nv*32),dtype='<f4').reshape(-1,8).astype(float)
            triangles=np.frombuffer(f.read(ni*4),dtype='<i4').reshape(-1,3)
            parts.append(dict(name=name,module=module,attachment=attachment,material=material,ruin=ruin,conditions=conditions,v=vertices,t=triangles))
        assert not f.read(1)
    return surfaces,parts

def module_position(module):
    if module==0:return np.zeros(3)
    if module<=4:
        a=(module-1)*math.pi/2;return np.array((math.sin(a)*12,0,math.cos(a)*12))
    return np.array([(-10,0,9),(10,0,9),(-10,0,-9),(10,0,-9),(-19,0,0),(-19,0,-12),(-19,0,12),(19,0,-12),(19,0,12),(19,0,0),(0,0,-21)][module-5],float)

def built(module,ranks):
    if module<=5:return True
    if module==6:return ranks[3]>=2
    linked={7:[4],8:[5],9:[6,16,25],10:[11,12],11:[7,8,9,10],12:[13,14,15,24],13:[17,18,19,20],14:[21,22],15:[23]}
    return any(ranks[i]>0 for i in linked[module])

def select(parts,ranks,ruined=()):
    selected=[]
    for part in parts:
        if not built(part['module'],ranks) or part['ruin']!=(part['module'] in ruined):continue
        if any(not minimum<=ranks[index]<=maximum for index,minimum,maximum in part['conditions']):continue
        values=part['v'];v=values[:,:3].copy();n=values[:,3:6].copy()
        if part['attachment']==1:v[:,1]+=3.65
        elif part['attachment']==2:v[:,1]+=4.5
        elif 1<=part['module']<=4:
            r=rotation((0,(part['module']-1)*90,0));v=v@r.T;n=n@r.T
        v+=module_position(part['module'])
        selected.append((part,v,n))
    return selected

def font(size,bold=False):
    return ImageFont.truetype('C:/Windows/Fonts/'+('segoeuib.ttf' if bold else 'segoeui.ttf'),size)

def render(parts,surfaces,ranks,path,title,yaw=34,pitch=28,size=(1440,1050),ruined=(),detail=False,framing=None):
    selected=select(parts,ranks,ruined)
    camera=rotation((pitch,0,0))@rotation((0,-yaw,0))
    # Camera looks towards negative view Z. Camera pitch brings roof details into view.
    all_vertices=np.concatenate([v for p,v,n in selected])@camera.T
    lo,hi=(all_vertices.min(0),all_vertices.max(0)) if framing is None else framing
    center=(lo+hi)*.5
    ss=2;w,h=size[0]*ss,size[1]*ss
    scale=min((w-90*ss)/(hi[0]-lo[0]),(h-160*ss)/(hi[1]-lo[1]))
    image=np.zeros((h,w,3),np.float32)
    y=np.linspace(0,1,h)[:,None,None]
    image[:]=np.array((15,24,32))[None,None,:]+(1-y)*np.array((13,13,16))[None,None,:]
    depth=np.full((h,w),-1e20,np.float32);glow=np.zeros((h,w,3),np.uint8)
    key=unit((-.45,.8,1));fill=unit((.8,.25,-.5));half=unit(key+(0,0,1))
    textures={i:np.asarray(Image.open(ROOT/f'assets/castle_{name}.png').convert('RGB'),float)/255 for i,(name,_) in enumerate(surfaces)}
    for part,vertices,normals in selected:
        v=vertices@camera.T;n=normals@camera.T
        screen=np.column_stack((w*.5+(v[:,0]-center[0])*scale,h*.54-(v[:,1]-center[1])*scale,v[:,2]))
        name,surface=surfaces[part['material']];rgb=np.array(surface[:3])*255;smooth,emission=surface[3:]
        shade=.38+.62*np.maximum(n@key,0)+.16*np.maximum(n@fill,0)
        if emission>.5:shade=.9+.1*np.maximum(n@key,0)
        colors=rgb*shade[:,None]
        colors+=255*(.25 if name=='brass' else .10)*np.maximum(n@half,0)[:,None]**(32 if smooth>.4 else 18)
        texture=textures[part['material']];uv=part['v'][:,6:8]
        triangles=part['t'];direction=np.cross(v[triangles[:,1]]-v[triangles[:,0]],v[triangles[:,2]]-v[triangles[:,0]])[:,2]
        for ids in triangles[direction>1e-9]:
            tri=screen[ids];a,b,c=tri
            x0=max(0,int(np.floor(tri[:,0].min())));x1=min(w,int(np.ceil(tri[:,0].max()))+1)
            y0=max(0,int(np.floor(tri[:,1].min())));y1=min(h,int(np.ceil(tri[:,1].max()))+1)
            if x1<=x0 or y1<=y0:continue
            den=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
            if abs(den)<1e-9:continue
            yy,xx=np.mgrid[y0:y1,x0:x1];xx=xx+.5;yy=yy+.5
            u=((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1]))/den
            vv=((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1]))/den;q=1-u-vv
            z=u*a[2]+vv*b[2]+q*c[2]
            valid=(u>=0)&(vv>=0)&(q>=0)&(z>depth[y0:y1,x0:x1])
            if not valid.any():continue
            pixel=u[:,:,None]*colors[ids[0]]+vv[:,:,None]*colors[ids[1]]+q[:,:,None]*colors[ids[2]]
            coords=(u[:,:,None]*uv[ids[0]]+vv[:,:,None]*uv[ids[1]]+q[:,:,None]*uv[ids[2]])%1
            tx=(coords[:,:,0]*texture.shape[1]).astype(int)%texture.shape[1];ty=((1-coords[:,:,1])*texture.shape[0]).astype(int)%texture.shape[0]
            pixel*=texture[ty,tx]
            depth[y0:y1,x0:x1][valid]=z[valid];image[y0:y1,x0:x1][valid]=pixel[valid]
            glow[y0:y1,x0:x1][valid]=np.uint8(np.clip(rgb*min(.6,emission*.25),0,255))
    image+=np.asarray(Image.fromarray(glow).filter(ImageFilter.GaussianBlur(ss*4)),float)*.55
    result=Image.fromarray(np.uint8(np.clip(image,0,255))).resize(size,Image.Resampling.LANCZOS)
    draw=ImageDraw.Draw(result);draw.text((34,20),title,font=font(28,True),fill='#dce8e8')
    count=sum(len(p['t']) for p,v,n in selected)
    draw.text((34,61),f'Actual runtime meshes • {count:,} triangles • original architectural assets',font=font(16),fill='#89b2c4')
    draw.text((34,size[1]-36),'Studio render of the packaged geometry and surfaces; game lighting and native trade stations differ.',font=font(14),fill='#8a9da9')
    result.save(path)
    print(path.name,count,'triangles',flush=True)
    return result

def main():
    surfaces,parts=read_asset();starter=[0]*26;starter[3]=1
    middle=[3,1,2,2,1,1,2,1,1,1,0,1,0,1,1,0,1,1,0,0,0,1,0,0,0,0]
    jobs=[('01-starter.png',starter,'01 / THE FIRST KEEP'),('02-developed.png',middle,'02 / A DEVELOPING STRONGHOLD'),('03-citadel.png',MAXIMA,'03 / THE COMPLETED CITADEL')]
    full=np.concatenate([v for p,v,n in select(parts,MAXIMA)])@(rotation((28,0,0))@rotation((0,-34,0))).T
    framing=(full.min(0),full.max(0))
    images=[]
    for filename,ranks,title in jobs:images.append(render(parts,surfaces,ranks,OUT/filename,title,framing=framing))
    sheet=Image.new('RGB',(2160,880),(12,20,28));d=ImageDraw.Draw(sheet)
    d.text((30,18),'CASTLE GAUNTLET / VISIBLE PROGRESSION',font=font(29,True),fill='#dce8e8')
    d.text((30,60),'Actual runtime meshes. Identical camera, scale and lighting. Every purchased rank adds geometry.',font=font(17),fill='#8fb8c9')
    for i,im in enumerate(images):sheet.paste(im.resize((720,525),Image.Resampling.LANCZOS),(i*720,115))
    notes=['Keep, arched entrances and one repeater.','Reinforced ramparts, towers and open utility wings.','Crowned keep, specialized machinery and expanded halls.']
    for i,text in enumerate(notes):d.text((i*720+27,686),text,font=font(17),fill='#d4e0e5')
    d.text((30,805),'Offline model preview. Native shops/stations are spawned separately in-game. No game launched.',font=font(16),fill='#8799a7')
    sheet.save(OUT/'00-progression.png')
    render(parts,surfaces,MAXIMA,OUT/'04-rear.png','COMPLETED CITADEL / REAR',yaw=160)
    import hashlib
    report={'source':'assets/castle.meshbin','modelSha256':hashlib.sha256((ROOT/'assets/castle.meshbin').read_bytes()).hexdigest(),'actualRuntimeGeometry':True,'gameLaunched':False,'renders':[name for name,_,_ in jobs]+['00-progression.png','04-rear.png']}
    (OUT/'preview-info.json').write_text(json.dumps(report,indent=2))

if __name__=='__main__':main()
