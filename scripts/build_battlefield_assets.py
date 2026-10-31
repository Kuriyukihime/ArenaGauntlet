"""Original seamless grass/earth surface for the mode-local flat battlefield."""
from pathlib import Path
import sys,json
root=Path(__file__).resolve().parent.parent
sys.path.insert(0,str(root/'.build/python'))
import numpy as np
from PIL import Image

size=1024
rng=np.random.default_rng(901)
yy,xx=np.mgrid[0:size,0:size]/size
# Integer-frequency harmonics keep opposite edges seamless at every mip level.
broad=np.zeros((size,size),float)
for frequency,strength in ((1,.06),(2,.04),(4,.03),(8,.02),(16,.015)):
    broad+=strength*(np.sin(2*np.pi*frequency*xx+.7)*np.cos(2*np.pi*frequency*yy+1.4))
fine=rng.normal(0,.018,(size,size))
base=np.array([.33,.39,.25])[None,None,:]
soil=np.maximum(0,np.sin(2*np.pi*(xx*3+yy*2))*.65+np.cos(2*np.pi*(xx*2-yy*5))*.35-.52)
pixels=base+broad[:,:,None]+fine[:,:,None]+soil[:,:,None]*np.array([.08,-.025,-.045])[None,None,:]
pixels=np.clip(pixels*255,0,255).astype('uint8')
out=root/'assets/castle_battlefield_ground.png';Image.fromarray(pixels,'RGB').save(out,optimize=True)
(root/'.build/battlefield-art.json').write_text(json.dumps({'surface':str(out.relative_to(root)),'size':size,'originalProceduralArt':True,'topRenderers':4,'groundIsLevel':True},indent=2),encoding='utf-8')
print('Built original 1024px grass/earth battlefield surface; four batched render surfaces.')
