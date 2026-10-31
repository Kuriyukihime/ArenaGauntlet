"""GPU studio renders of the exact packaged Hellenic arena, not game screenshots.
Uses the original diffuse and AG normal maps; native grass is a local reference.
"""
from pathlib import Path
import sys,struct,json,hashlib,math
import numpy as np
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parent.parent
sys.path.insert(0,str(ROOT/'.build/render-python'))
import moderngl

def read_asset(path):
 with path.open('rb') as f:
  def read(fmt):return struct.unpack('<'+fmt,f.read(struct.calcsize('<'+fmt)))
  def string():
   count=shift=0
   while True:
    byte=read('B')[0];count|=(byte&127)<<shift
    if byte<128:break
    shift+=7
   return f.read(count).decode()
  assert f.read(5)==b'CAST1';surfaces=[(string(),read('5f')) for _ in range(read('i')[0])];parts=[]
  for _ in range(read('i')[0]):
   name=string();module,attachment,material,ruin=read('iii?')
   conditions=[read('iii') for _ in range(read('i')[0])];nv,ni=read('ii')
   v=np.frombuffer(f.read(nv*32),dtype='<f4').reshape(-1,8).copy()
   t=np.frombuffer(f.read(ni*4),dtype='<i4').copy()
   parts.append(dict(name=name,material=material,v=v,t=t))
  assert not f.read(1)
 return surfaces,parts

def unit(v):
 v=np.array(v,float);return v/np.linalg.norm(v)
def look(eye,target):
 eye=np.array(eye,float);f=unit(np.array(target)-eye);s=unit(np.cross(f,(0,1,0)));u=np.cross(s,f)
 m=np.eye(4);m[:3,:3]=np.array((s,u,-f));m[:3,3]=-m[:3,:3]@eye;return m
def ortho(l,r,b,t,n,f):
 return np.array([[2/(r-l),0,0,-(r+l)/(r-l)],[0,2/(t-b),0,-(t+b)/(t-b)],
                  [0,0,-2/(f-n),-(f+n)/(f-n)],[0,0,0,1]],float)
def perspective(fov,aspect,near,far):
 q=1/math.tan(math.radians(fov)*.5)
 return np.array([[q/aspect,0,0,0],[0,q,0,0],[0,0,(far+near)/(near-far),2*far*near/(near-far)],[0,0,-1,0]],float)

VERT='''#version 330
in vec3 in_pos; in vec3 in_normal; in vec2 in_uv;
uniform mat4 vp; uniform mat4 light_vp;
out vec3 world;out vec3 normal;out vec2 uv;out vec4 shadow_pos;
void main(){world=in_pos;normal=in_normal;uv=in_uv;shadow_pos=light_vp*vec4(in_pos,1);gl_Position=vp*vec4(in_pos,1);}
'''
FRAG='''#version 330
uniform sampler2D albedo;uniform sampler2D bump;uniform sampler2DShadow shadowmap;
uniform vec3 tint;uniform vec3 eye;uniform vec3 sun;uniform float strength;
in vec3 world;in vec3 normal;in vec2 uv;in vec4 shadow_pos;out vec4 color;
void main(){
 vec3 n=normalize(normal);vec3 dp1=dFdx(world),dp2=dFdy(world);vec2 duv1=dFdx(uv),duv2=dFdy(uv);
 vec3 p=cross(dp2,n),q=cross(n,dp1);vec3 T=p*duv1.x+q*duv2.x,B=p*duv1.y+q*duv2.y;
 float scale=inversesqrt(max(max(dot(T,T),dot(B,B)),.000001));mat3 tbn=mat3(T*scale,B*scale,n);
 vec4 normal_sample=texture(bump,uv);vec2 xy=(normal_sample.ag*2.-1.)*strength;
 n=normalize(tbn*vec3(xy,sqrt(max(.01,1.-dot(xy,xy)))));
 vec3 s=shadow_pos.xyz/shadow_pos.w*.5+.5;float visible=0.;
 float bias=max(.00013,.00085*(1.-max(dot(n,sun),0.)));
 for(int x=-2;x<=2;x++)for(int y=-2;y<=2;y++)visible+=texture(shadowmap,vec3(s.xy+vec2(x,y)/4096.,s.z-bias));
 visible/=25.;if(s.x<0.||s.x>1.||s.y<0.||s.y>1.)visible=1.;
 vec3 base=pow(texture(albedo,uv).rgb*tint,vec3(2.2));
 vec3 ambient=mix(vec3(.07,.08,.06),vec3(.25,.32,.39),n.y*.5+.5);
 float direct=max(dot(n,sun),0.);vec3 light=ambient+vec3(1.10,1.00,.80)*direct*visible;
 vec3 lit=base*light*1.65;lit=lit/(lit+vec3(.60));lit=pow(lit,vec3(1./2.2));
 float fog=1.-exp(-distance(world,eye)*.00013);lit=mix(lit,vec3(.65,.72,.75),fog);
 color=vec4(lit,1.);
}
'''
SHADOW_VERT='''#version 330
in vec3 in_pos;uniform mat4 vp;void main(){gl_Position=vp*vec4(in_pos,1);}
'''
SHADOW_FRAG='''#version 330
void main(){}
'''

def main():
 surfaces,parts=read_asset(ROOT/'assets/colosseum.meshbin')
 # Show the runtime shopping pose: shutter batches retract below the floor.
 for part in parts:
  if part['name'].startswith('MerchantShutters'):part['v'][:,1]-=7.5
 out=ROOT/'dist/arena-model-review';out.mkdir(exist_ok=True)
 ctx=moderngl.create_standalone_context(require=330)
 program=ctx.program(vertex_shader=VERT,fragment_shader=FRAG)
 shadow_program=ctx.program(vertex_shader=SHADOW_VERT,fragment_shader=SHADOW_FRAG)
 arrays=[];shadow_arrays=[]
 for part in parts:
  vbo=ctx.buffer(part['v'].astype('f4').tobytes());ibo=ctx.buffer(part['t'].astype('i4').tobytes())
  arrays.append(ctx.vertex_array(program,[(vbo,'3f 3f 2f','in_pos','in_normal','in_uv')],ibo))
  shadow_arrays.append(ctx.vertex_array(shadow_program,[(vbo,'3f 20x','in_pos')],ibo))
 textures=[]
 for name,_ in surfaces:
  path=(ROOT/'.build/native-grass-reference.png') if name=='grass' else ROOT/f'assets/arena_{name}.png'
  im=Image.open(path).convert('RGB').transpose(Image.Transpose.FLIP_TOP_BOTTOM)
  tex=ctx.texture(im.size,3,im.tobytes());tex.build_mipmaps();tex.filter=(moderngl.LINEAR_MIPMAP_LINEAR,moderngl.LINEAR);tex.anisotropy=8
  if name=='grass':bump=Image.new('RGBA',(4,4),(255,128,255,128))
  else:bump=Image.open(ROOT/f'assets/arena_{name}_normal.png').convert('RGBA')
  bump=bump.transpose(Image.Transpose.FLIP_TOP_BOTTOM);normal=ctx.texture(bump.size,4,bump.tobytes());normal.build_mipmaps()
  textures.append((tex,normal))
 sun=unit((-.6,1,-.7));light=ortho(-350,350,-350,350,1,1600)@look(sun*700,(0,0,0))
 depth=ctx.depth_texture((4096,4096));depth.compare_func='<=';depth.repeat_x=depth.repeat_y=False
 sfbo=ctx.framebuffer(depth_attachment=depth);sfbo.use();sfbo.clear(depth=1)
 ctx.enable(moderngl.DEPTH_TEST);ctx.disable(moderngl.CULL_FACE)
 shadow_program['vp'].write(light.T.astype('f4').tobytes())
 # Flush grass/inlays need no separate cast shadow; the floor is continuous.
 for part,vao in zip(parts,shadow_arrays):
  if not part['name'].startswith('Courtyard'):vao.render()
 jobs=[
  ('01-overview.png','ARENAGAUNTLET / THE HELLENIC ARENA',(500,530,590),(0,7,0),42,'ortho'),
  ('02-propylon.png','DORIC PROPYLON / CARVED STONE',(45,22,156),(0,15,222),48,'perspective'),
  ('03-courtyard.png','STONE COURTYARD / GRASS INSETS',(91,5,78),(-8,20,216),65,'perspective'),
  ('04-column-detail.png','FLUTES / ENTASIS / DORIC CAPITAL',(34,11,191),(25,10,212.6),44,'perspective'),
  ('05-reverse.png','REVERSE / STONE CAVEA AND PERISTYLE',(-500,460,-520),(0,8,0),42,'ortho'),
  ('06-merchant-alcoves.png','MERCHANT ARCADE / FIVE RECESSED STONE BAYS',(39,12,-139),(0,4,-182),60,'perspective'),
 ]
 width,height=1680,1120;ss=2;size=(width*ss,height*ss)
 color=ctx.texture(size,4);zbuffer=ctx.depth_renderbuffer(size);frame=ctx.framebuffer([color],zbuffer)
 program['light_vp'].write(light.T.astype('f4').tobytes());program['sun'].value=tuple(sun)
 program['albedo'].value=0;program['bump'].value=1;program['shadowmap'].value=2
 for filename,title,eye,target,fov,kind in jobs:
  projection=ortho(-255*width/height,255*width/height,-255,255,1,2000) if kind=='ortho' else perspective(fov,width/height,.3,1200)
  frame.use();frame.clear(.15,.20,.23,1,depth=1);program['vp'].write((projection@look(eye,target)).T.astype('f4').tobytes());program['eye'].value=eye;depth.use(2)
  for part,vao in zip(parts,arrays):
   index=part['material'];name,mat=surfaces[index];textures[index][0].use(0);textures[index][1].use(1)
   program['tint'].value=mat[:3];program['strength'].value=.0 if name=='grass' else .45;vao.render()
  image=Image.frombytes('RGBA',size,frame.read(components=4)).transpose(Image.Transpose.FLIP_TOP_BOTTOM).convert('RGB')
  image=image.resize((width,height),Image.Resampling.LANCZOS);draw=ImageDraw.Draw(image)
  font=lambda n,b=False:ImageFont.truetype('C:/Windows/Fonts/'+('segoeuib.ttf' if b else 'segoeui.ttf'),n)
  draw.rectangle((0,0,width,92),fill='#111e25');draw.text((34,17),title,font=font(28,True),fill='#f0eadc')
  draw.text((35,56),'Original packaged meshes and stone surfaces | studio lighting | no game launched',font=font(17),fill='#a9b8ad')
  image.save(out/filename);print(filename,flush=True)
 report=dict(actualRuntimeGeometry=True,gameLaunched=False,source='assets/colosseum.meshbin',
             sha256=hashlib.sha256((ROOT/'assets/colosseum.meshbin').read_bytes()).hexdigest(),renderer=ctx.info['GL_RENDERER'],
             renders=[job[0] for job in jobs],surfaces={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (ROOT/'assets').glob('arena_*.png')})
 (out/'preview-info.json').write_text(json.dumps(report,indent=2),encoding='utf-8');ctx.release()

if __name__=='__main__':main()
