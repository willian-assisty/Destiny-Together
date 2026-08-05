"""
Separa as CONCHAS de um FBX de malha unica em varios .obj.

Existe porque o gerador entregou o mago com os cristais grudados no corpo, e cristal grudado nao
orbita: ele vira parte da silhueta. Separar em arquivos deixa o corpo ser corpo e os cristais serem
pecas que o CrystalOrbit posiciona todo frame.

Cada cristal sai RECENTRADO na propria origem — o componente escreve a posicao de mundo deles, e
uma peca cuja geometria esta a meio metro do proprio pivot orbitaria em volta de um ponto errado.
"""
import sys, os, struct
from collections import defaultdict
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fbxmeasure import read_node

MIN_SHELL = 40          # abaixo disto e caco de malha, nao peca

def parse(path):
    with open(path, 'rb') as f: data = f.read()
    ver = struct.unpack_from('<I', data, 23)[0]
    pos, roots = 27, []
    while True:
        node, pos, _ = read_node(data, pos, ver)
        if node is None: break
        roots.append(node)
    return roots

def name_of(n):
    return n[0].decode('utf-8','replace') if isinstance(n[0],(bytes,bytearray)) else n[0]

def collect(path):
    got = {}
    def walk(n):
        if n is None: return
        nm = name_of(n)
        if nm in ('Vertices','PolygonVertexIndex','Normals','NormalsIndex','UV','UVIndex',
                  'MappingInformationType','ReferenceInformationType') and n[1]:
            got.setdefault(nm, []).append(n[1][0])
        for c in n[2]: walk(c)
    for r in parse(path): walk(r)
    return got

g = collect(sys.argv[1])
verts = g['Vertices'][0]
idx = g['PolygonVertexIndex'][0]
normals = g.get('Normals', [None])[0]
nrmidx = g.get('NormalsIndex', [None])[0]
uvs = g.get('UV', [None])[0]
uvidx = g.get('UVIndex', [None])[0]

nv = len(verts)//3
faces, cur = [], []
for i in idx:
    if i < 0: cur.append(-i-1); faces.append(cur); cur = []
    else: cur.append(i)

# --- conchas -------------------------------------------------------------------------------
parent = list(range(nv))
def find(a):
    while parent[a] != a: parent[a] = parent[parent[a]]; a = parent[a]
    return a
def union(a,b):
    ra, rb = find(a), find(b)
    if ra != rb: parent[rb] = ra
for f in faces:
    for k in range(1,len(f)): union(f[0], f[k])

comp = defaultdict(list)
for v in range(nv): comp[find(v)].append(v)
shells = sorted(((len(vs), root) for root, vs in comp.items()), reverse=True)
shells = [(n, r) for n, r in shells if n >= MIN_SHELL]

print(f"conchas com >= {MIN_SHELL} vertices: {len(shells)}")

# Faces por concha, mais o indice de polygon-vertex de cada canto (para normal/UV).
pv_base = []
acc = 0
for f in faces:
    pv_base.append(acc); acc += len(f)

shell_faces = defaultdict(list)
for fi, f in enumerate(faces):
    shell_faces[find(f[0])].append(fi)

def write_obj(path, root, recenter):
    fs = shell_faces[root]
    used = {}
    order = []
    for fi in fs:
        for v in faces[fi]:
            if v not in used:
                used[v] = len(order)+1
                order.append(v)

    cx = cy = cz = 0.0
    if recenter:
        xs = [verts[3*v] for v in order]; ys = [verts[3*v+1] for v in order]; zs = [verts[3*v+2] for v in order]
        cx, cy, cz = (min(xs)+max(xs))/2, (min(ys)+max(ys))/2, (min(zs)+max(zs))/2

    # normal e UV sao por POLYGON-VERTEX neste arquivo; sao reindexados junto com a face.
    nrm_out, uv_out = [], []
    nmap, umap = {}, {}

    lines = []
    for fi in fs:
        f = faces[fi]
        parts = []
        for k, v in enumerate(f):
            pv = pv_base[fi] + k
            vi = used[v]

            ui = ''
            if uvs is not None:
                src = uvidx[pv] if uvidx is not None else pv
                if src not in umap:
                    umap[src] = len(uv_out)+1
                    uv_out.append((uvs[2*src], uvs[2*src+1]))
                ui = str(umap[src])

            ni = ''
            if normals is not None:
                # IndexToDirect: o canto aponta para uma normal da tabela, e nao para a
                # posicao dele proprio. Ler direto por `pv` estoura o vetor — foi o que
                # aconteceu na primeira tentativa.
                src = nrmidx[pv] if nrmidx is not None else pv
                if src not in nmap:
                    nmap[src] = len(nrm_out)+1
                    nrm_out.append((normals[3*src], normals[3*src+1], normals[3*src+2]))
                ni = str(nmap[src])

            parts.append(f"{vi}/{ui}/{ni}" if (ui or ni) else str(vi))
        # triangula em leque
        for k in range(1, len(parts)-1):
            lines.append(f"f {parts[0]} {parts[k]} {parts[k+1]}")

    with open(path, 'w', encoding='utf-8') as o:
        o.write(f"# separado de {os.path.basename(sys.argv[1])}\n")
        for v in order:
            o.write(f"v {verts[3*v]-cx:.6f} {verts[3*v+1]-cy:.6f} {verts[3*v+2]-cz:.6f}\n")
        for u in uv_out: o.write(f"vt {u[0]:.6f} {u[1]:.6f}\n")
        for n in nrm_out: o.write(f"vn {n[0]:.6f} {n[1]:.6f} {n[2]:.6f}\n")
        o.write("\n".join(lines) + "\n")

    return len(order), len(lines), (cx, cy, cz)

outdir = sys.argv[2]
os.makedirs(outdir, exist_ok=True)
os.makedirs(os.path.join(outdir, "Cristais"), exist_ok=True)

n, t, _ = write_obj(os.path.join(outdir, "Mago.obj"), shells[0][1], recenter=False)
print(f"  corpo    Mago.obj                {n:>7,} vert  {t:>7,} tri")

for i, (cnt, root) in enumerate(shells[1:4], start=1):
    p = os.path.join(outdir, "Cristais", f"Cristal{i}.obj")
    n, t, c = write_obj(p, root, recenter=True)
    print(f"  cristal  Cristais/Cristal{i}.obj   {n:>7,} vert  {t:>7,} tri   "
          f"centro original ({c[0]:+.3f},{c[1]:+.3f},{c[2]:+.3f})")
