"""MIT, GamePanel contributors, 2026. Original procedural asphalt, no source images.
Integer-only construction and BC1 encoding; deterministic on Windows/Linux.
NumPy 2.3.5, CPython 3.12.14. DDS 2048x2048, nine mips down to 8x8.
"""
from pathlib import Path
import struct
import numpy as np
SIZE=2048
NAMES=(('INTERSECT',22011),('LANE',22012),('PARKING',22013))

def hash_grid(x,y,seed):
    v=(x.astype('uint64')*0x1f123bb5+y.astype('uint64')*0x5f356495+seed)&0xffffffff
    v=((v^(v>>16))*0x7feb352d)&0xffffffff
    v=((v^(v>>15))*0x846ca68b)&0xffffffff
    return ((v^(v>>16))&255).astype('int32')

def noise(seed,spacing):
    coord=np.arange(SIZE,dtype='int32')
    grid=coord//spacing;weight=coord%spacing
    cells=SIZE//spacing
    a=hash_grid(grid[None,:],grid[:,None],seed)
    b=hash_grid((grid[None,:]+1)%cells,grid[:,None],seed)
    c=hash_grid(grid[None,:],(grid[:,None]+1)%cells,seed)
    d=hash_grid((grid[None,:]+1)%cells,(grid[:,None]+1)%cells,seed)
    upper=(a*(spacing-weight[None,:])+b*weight[None,:])//spacing
    lower=(c*(spacing-weight[None,:])+d*weight[None,:])//spacing
    return (upper*(spacing-weight[:,None])+lower*weight[:,None])//spacing-128

def asphalt(seed):
    fine=noise(seed,1)
    grain=noise(seed+7,2)
    value=54+fine//18+grain//10+noise(seed+13,16)//24+noise(seed+19,128)//28
    value+=np.maximum(grain-20,0)//7
    value-=np.maximum(-grain-75,0)//12
    hue=noise(seed+29,8)//70
    return np.clip(np.stack((value+hue,value,value-hue),axis=-1),20,110).astype('uint8')

def bc1(a):
    h,w,_=a.shape
    blocks=a.astype('int32').reshape(h//4,4,w//4,4,3).transpose(0,2,1,3,4).reshape(-1,16,3)
    luma=blocks.sum(axis=2)
    lo=blocks[np.arange(len(blocks)),luma.argmin(axis=1)]
    hi=blocks[np.arange(len(blocks)),luma.argmax(axis=1)]
    def pack565(c):return ((c[:,0]>>3)<<11)|((c[:,1]>>2)<<5)|(c[:,2]>>3)
    c0=pack565(hi).astype('uint16');c1=pack565(lo).astype('uint16')
    c0=np.where(c0==c1,np.minimum(c0.astype('int32')+1,65535),c0).astype('uint16')
    def unpack(c):return np.stack((((c>>11)&31)*255//31,((c>>5)&63)*255//63,(c&31)*255//31),axis=1).astype('int32')
    p0=unpack(c0.astype('int32'));p1=unpack(c1.astype('int32'))
    palette=np.stack((p0,p1,(2*p0+p1)//3,(p0+2*p1)//3),axis=1)
    indices=((blocks[:,:,None,:]-palette[:,None,:,:])**2).sum(axis=3).argmin(axis=2).astype('uint32')
    codes=np.sum(indices<<(np.arange(16,dtype='uint32')*2),axis=1,dtype='uint32')
    result=np.empty(len(blocks),dtype=[('c0','<u2'),('c1','<u2'),('indices','<u4')])
    result['c0']=c0;result['c1']=c1;result['indices']=codes
    return result.tobytes()

def dds(a,path):
    h,w,_=a.shape;levels=[]
    while min(a.shape[:2])>=8:
        levels.append(bc1(a))
        # Exact 2x2 box mips: no floating-point or external image encoders.
        a=(a[::2,::2].astype('uint16')+a[1::2,::2]+a[::2,1::2]+a[1::2,1::2])//4
        a=a.astype('uint8')
    header=bytearray(128);header[:4]=b'DDS '
    struct.pack_into('<7I',header,4,124,0xA1007,h,w,w*h//2,0,len(levels))
    struct.pack_into('<II4s5I',header,76,32,4,b'DXT1',0,0,0,0,0)
    struct.pack_into('<I',header,108,0x401008)
    path.write_bytes(header+b''.join(levels))

def generate(root):
    output=Path(root)/'original-asphalt';output.mkdir(parents=True,exist_ok=True)
    for suffix,seed in NAMES:
        dds(asphalt(seed),output/f'GP_A8_ASPHALT_{suffix}.dds')

if __name__=='__main__':
    import sys
    generate(Path(sys.argv[1]) if len(sys.argv)>1 else Path.cwd())
