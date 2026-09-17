"""Frozen diagnostic: independent pixels, system instructions, neighbors, full canvas.

Run from the repository root. Uses the pinned large model, Vulkan, an 8x8 grid,
and a seeded random pixel order. These authored cases are not a quality guarantee.
"""
import sys, json, random, time, struct, zlib
from pathlib import Path
sys.path.insert(0, 'bindings/python')
sys.path.insert(0, 'scripts')
from eugeniusz import Runtime
from benchmark_pixel_positions import prompts, COLORS, HEX

def png(path, pixels, width, height):
    def chunk(name,data): return struct.pack('>I',len(data))+name+data+struct.pack('>I',zlib.crc32(name+data)&0xffffffff)
    raw=b''.join(b'\0'+bytes(v for rgb in pixels[y*width:(y+1)*width] for v in rgb) for y in range(height))
    path.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',width,height,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(raw))+chunk(b'IEND',b''))

OUT=Path('build/pixel-experiments'); OUT.mkdir(parents=True,exist_ok=True)
SYSTEM='''You are a pixel-art renderer. The state is a drawing specification, not a classification document. Render the specified target pixel as part of ONE consistent composition. Read row and column carefully: row 0 is top, column 0 is left. Keep background outside objects and place recognizable connected silhouettes inside their intended bounds. Do not color every pixel with the object's main color. Commit to consistent object positions across the canvas. Existing colored pixels are partial observations, not examples to copy everywhere. Unknown pixels are not black. Preserve distinct foreground/background boundaries. If the specification contains explicit geometry, obey its coordinate bounds exactly. Select the palette entry for the target location.'''
CASES=[('square','A red square centered on a sky blue background. The square covers rows 2 through 5 and columns 2 through 5 inclusive.'),
       ('flag','A flag made of three equal vertical stripes: blue on the left, white in the middle, red on the right.'),
       ('apple','A single red apple with a small green leaf and a brown stem, centered on a sky blue background.'),
       ('house','A small white house with a red triangular roof and a brown door, standing on green grass under a blue sky.')]
N=8
def expected(name,x,y):
    if name=='square':return 7 if 2<=x<=5 and 2<=y<=5 else 15
    if name=='flag':return 2 if x<3 else 11 if x<5 else 7
    return None
def state_new(desc,idx,canvas,mode):
    x,y=idx%N,idx//N
    s=f'IMAGE: {desc}\nCanvas: {N} columns wide and {N} rows high. Coordinates are zero-based.\nTARGET: row={y}, column={x}.\n'
    if mode=='neighbors':
        s+='Already painted neighbors (unlisted cells are unknown):\n'
        for dy in range(-2,3):
            for dx in range(-2,3):
                a,b=x+dx,y+dy
                if 0<=a<N and 0<=b<N and canvas[b*N+a]>=0:
                    s+=f'row={b}, column={a}: {COLORS[canvas[b*N+a]]}\n'
    if mode=='canvas':
        s+='Painted canvas, one row per line; ? is unknown, @ is the target. Letters are palette option letters.\n'
        s+='    '+''.join(map(str,range(N)))+'\n'
        for row in range(N):
            s+=f'{row}:  '+''.join('@' if row*N+c==idx else '?' if canvas[row*N+c]<0 else chr(65+canvas[row*N+c]) for c in range(N))+'\n'
        s+='Palette: '+', '.join(f'{chr(65+i)}={c}' for i,c in enumerate(COLORS))+'\n'
    return s

report=[]
for mode in ['baseline','system','neighbors','canvas']:
    with Runtime('build/install-vulkan').load_model('models/downloads/Qwen3-4B-Q4_K_M.gguf',gpu_layers=99,system_prompt=None if mode=='baseline' else SYSTEM) as m:
        for name,desc in CASES:
            canvas=[-1]*64; order=list(range(64));random.Random(42).shuffle(order);t=time.monotonic()
            for i in order:
                x,y=i%N,i//N
                if mode=='baseline':state,q=prompts(desc,x,y,True)
                else:state,q=state_new(desc,i,canvas,mode),f'Render the image specification at row {y}, column {x}. Which palette color belongs at this target pixel?'
                r=m.choice(state,q,[f'{c} ({h})' for c,h in zip(COLORS,HEX)])
                canvas[i]=r.choice
            correct=sum(canvas[y*N+x]==expected(name,x,y) for y in range(N) for x in range(N)) if name in ('square','flag') else None
            record=dict(mode=mode,case=name,description=desc,colors=canvas,correct=correct,seconds=round(time.monotonic()-t,2))
            report.append(record);(OUT/'report.json').write_text(json.dumps(report,indent=2))
            png(OUT/f'{name}-{mode}.png',[tuple(bytes.fromhex(HEX[canvas[(y//32)*8+x//32]][1:])) for y in range(256) for x in range(256)],256,256)
            print(mode,name,'correct',correct,'seconds',record['seconds'],flush=True)
width=4*276; height=4*276; sheet=[(255,255,255)]*(width*height)
for r in report:
    col=['baseline','system','neighbors','canvas'].index(r['mode']);row=[n for n,_ in CASES].index(r['case'])
    for y in range(256):
        for x in range(256):sheet[(row*276+10+y)*width+col*276+10+x]=tuple(bytes.fromhex(HEX[r['colors'][(y//32)*8+x//32]][1:]))
png(OUT/'comparison.png',sheet,width,height)
