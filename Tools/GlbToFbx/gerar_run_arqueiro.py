"""
Ciclo de CORRIDA (v3) para o personagem Meshy/UniRig.
v3 adiciona: janelas de apoio por perna, travamento do pe por IK no tornozelo
(sem flutuar e sem afundar), arco balistico de voo e sobreposicao de fases.
"""
import numpy as np, math
from scipy.interpolate import CubicSpline
from pygltflib import GLTF2, Animation, AnimationSampler, AnimationChannel, \
    AnimationChannelTarget, Accessor, BufferView

SRC = "Meshy_AI_Character_output.glb"      # GLB original
OUT = "Meshy_AI_Character_RUN.glb"
CYCLE, NS, FINE = 0.62, 48, 720
LEAN, APEX = 17.0, 0.058
S0, S1 = 0.02, 0.26           # janela de apoio da perna A (perna B = +0.5)

def qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return np.array([aw*bx+ax*bw+ay*bz-az*by, aw*by-ax*bz+ay*bw+az*bx,
                     aw*bz+ax*by-ay*bx+az*bw, aw*bw-ax*bx-ay*by-az*bz])
def qconj(q): return np.array([-q[0], -q[1], -q[2], q[3]])
def qaxis(ax, deg):
    a = np.array(ax, float); a /= np.linalg.norm(a); h = math.radians(deg)*0.5
    return np.array([*(a*math.sin(h)), math.cos(h)])
def qrot(q, v):
    u = q[:3]; s = q[3]
    return 2*np.dot(u, v)*u + (s*s-np.dot(u, u))*v + 2*s*np.cross(u, v)
X, Y, Z = [1, 0, 0], [0, 1, 0], [0, 0, 1]

g = GLTF2().load(SRC)
byname = {n.name: i for i, n in enumerate(g.nodes)}
parent = {}
for i, n in enumerate(g.nodes):
    for c in (n.children or []): parent[c] = i
Lq = {i: np.array(n.rotation or [0, 0, 0, 1], float) for i, n in enumerate(g.nodes)}
Lt = {i: np.array(n.translation or [0, 0, 0], float) for i, n in enumerate(g.nodes)}
Gq, Gt = {}, {}
def build(i):
    if i in Gq: return
    if i in parent:
        build(parent[i]); p = parent[i]
        Gq[i] = qmul(Gq[p], Lq[i]); Gt[i] = Gt[p]+qrot(Gq[p], Lt[i])
    else: Gq[i], Gt[i] = Lq[i].copy(), Lt[i].copy()
for i in range(len(g.nodes)): build(i)

B = dict(root="Bone_000", spine1="Bone_006", spine2="Bone_005", spine3="Bone_004",
         chest="Bone_003", neck="Bone_019", head="Bone_018",
         thighA="Bone_016", shinA="Bone_015", footA="Bone_014", toeA="Bone_013",
         thighB="Bone_011", shinB="Bone_010", footB="Bone_009", toeB="Bone_008",
         clavA="Bone_024", uarmA="Bone_023", farmA="Bone_022", handA="Bone_021",
         clavB="Bone_029", uarmB="Bone_028", farmB="Bone_027", handB="Bone_026")
CAPE = ["Bone_035", "Bone_034", "Bone_033", "Bone_032", "Bone_031", "Bone_030"]
FING_A = [["Bone_045", "Bone_044", "Bone_043"], ["Bone_042", "Bone_041", "Bone_040"]]
FING_B = [["Bone_055", "Bone_054", "Bone_053"], ["Bone_052", "Bone_051", "Bone_050"]]
THUMB = (["Bone_038", "Bone_037", "Bone_036"], ["Bone_048", "Bone_047", "Bone_046"])
CHAIN = {"A": [55, 54, 53, 52, 51, 50, 49], "B": [55, 54, 48, 47, 46, 45, 44]}
SOLE = {n: Gt[n][1] for ch in CHAIN.values() for n in ch[-3:]}   # altura de repouso

UF = np.arange(FINE)/FINE
def curve(keys):
    u = np.array([k[0] for k in keys]+[1.0]); v = np.array([k[1] for k in keys]+[keys[0][1]])
    return CubicSpline(u, v, bc_type="periodic")(UF)
def at(c, u): return c[int(round((u % 1.0)*FINE)) % FINE]
def deriv(c): return (np.roll(c, -1)-np.roll(c, 1))*(FINE/2.0)
def lowpass(c, k):
    F = np.fft.rfft(c); F[k+1:] = 0; return np.fft.irfft(F, len(c))

# passada de corrida: quadril -52..+40, joelho ate 134, apoio ~32% do ciclo
THIGH = curve([(0.00, -32), (0.08, -14), (0.17, 6), (0.27, 30), (0.35, 40),
               (0.43, 16), (0.53, -14), (0.64, -45), (0.76, -52), (0.88, -48)])
KNEE  = curve([(0.00, 26), (0.08, 56), (0.17, 44), (0.27, 14), (0.35, 34),
               (0.43, 108), (0.53, 134), (0.64, 118), (0.76, 82), (0.88, 42)])
ANKLE = curve([(0.00, 4), (0.08, -12), (0.17, -8), (0.27, 30), (0.35, 40),
               (0.43, 12), (0.53, -4), (0.64, -10), (0.76, -12), (0.88, -6)])
TOE   = curve([(0.00, 0), (0.17, -5), (0.27, -18), (0.35, -24), (0.46, -4),
               (0.62, 0), (0.85, 0)])
DANK = {"A": np.zeros(FINE), "B": np.zeros(FINE)}   # correcao de IK do tornozelo
ROOTY = np.zeros(FINE)
VROOT = np.zeros(FINE)
ROOTX = 0.009*np.sin(2*np.pi*UF+0.4)

def root_rot(u):
    return qmul(qaxis(Y, -0.17*at(THIGH, u)), qaxis(Z, -0.032*at(THIGH, u-0.06)))

def leg_rots(u, side, extra=0.0):
    o = 0.0 if side == "A" else 0.5
    return (at(THIGH, u+o)-4, at(KNEE, u+o),
            at(ANKLE, u+o)+at(DANK[side], u)+extra, at(TOE, u+o))

def ground(u, side, extra=0.0):
    """Altura do ponto mais baixo do pe (0 = sola no chao), root em 0."""
    t, k, a, o = leg_rots(u, side, extra)
    rw = {CHAIN[side][0]: root_rot(u), CHAIN[side][2]: qaxis(X, t),
          CHAIN[side][3]: qaxis(X, k), CHAIN[side][4]: qaxis(X, a),
          CHAIN[side][5]: qaxis(X, o)}
    q = np.array([0, 0, 0, 1.0]); p = np.zeros(3); lows = []
    for n in CHAIN[side]:
        p = p + qrot(q, Lt[n])
        gp = Gq[parent[n]]
        lq = qmul(qmul(qmul(qconj(gp), rw[n]), gp), Lq[n]) if n in rw else Lq[n]
        q = qmul(q, lq)
        if n in SOLE: lows.append(p[1]-SOLE[n])
    return min(lows)

# ---- 1) curva de altura desejada: dip suave no apoio + arco balistico no voo
raw = np.array([-ground(u, "A") for u in UF])
def wrap(x): return (x+0.5) % 1.0-0.5
LSTANCE = S1-S0                       # duracao do apoio
FLEN = 0.5-LSTANCE                    # duracao da fase aerea
MID = (S0+S1)/2
pha = (UF-S0) % 1.0
sel = pha <= LSTANCE
c2 = np.polyfit(S0+pha[sel]-MID, raw[sel], 4)   # segue a geometria real da perna
hs, he = np.polyval(c2, S0-MID), np.polyval(c2, S1-MID)
des = np.zeros(FINE)
for i, u in enumerate(UF):
    pA = (u-S0) % 1.0; pB = (u-S0-0.5) % 1.0
    if pA <= LSTANCE:   des[i] = np.polyval(c2, S0+pA-MID)
    elif pB <= LSTANCE: des[i] = np.polyval(c2, S0+pB-MID)
    else:
        s = ((pA-LSTANCE) if pA < 0.5 else (pB-LSTANCE))/FLEN
        des[i] = he+(hs-he)*s+4*APEX*s*(1-s)
ROOTY = lowpass(des, 14)
print("root Y: min %.3f max %.3f amplitude %.3f" % (ROOTY.min(), ROOTY.max(), np.ptp(ROOTY)))

# ---- 2) IK do tornozelo: busca direta (a altura da sola nao e monotonica)
NC = 144
def solve_ankle(u, side, need, prev):
    """Escolhe o angulo que zera a folga penalizando salto em relacao ao quadro
    anterior: a altura da sola nao e monotonica (calcanhar x ponta do pe)."""
    grid = np.linspace(-25, 45, 141)
    cost = [abs(ground(u, side, a)-need)+0.0008*abs(a-prev) for a in grid]
    a0 = grid[int(np.argmin(cost))]
    fine = np.linspace(a0-1.0, a0+1.0, 21)
    c2f = [abs(ground(u, side, a)-need)+0.0008*abs(a-prev) for a in fine]
    return float(fine[int(np.argmin(c2f))])
for side in ("A", "B"):
    off = 0.0 if side == "A" else 0.5
    uc = np.arange(NC)/NC; dc = np.zeros(NC); prev = 0.0
    order = sorted(range(NC), key=lambda j: (uc[j]-off-S0) % 1.0)
    for j in order:
        uu = S0+((uc[j]-off-S0) % 1.0)
        if uu > S1: continue
        a = solve_ankle(uc[j], side, -at(ROOTY, uc[j]), prev); prev = a
        w = min(1.0, min(uu-S0, S1-uu)/0.045)
        dc[j] = a*(w*w*(3-2*w))
    DANK[side] = np.interp(UF, np.append(uc, 1.0), np.append(dc, dc[0]))
    DANK[side] = lowpass(DANK[side], 40)
VROOT = lowpass(deriv(ROOTY), 5)          # a capa e um sistema amortecido: filtra
VROOT /= max(abs(VROOT).max(), 1e-9)

# ---------------------------------------------------------------- pose completa
def world_rots(u):
    R = {}
    tA = at(THIGH, u)
    for side in ("A", "B"):
        t, k, a, o = leg_rots(u, side)
        R[byname[B["thigh"+side]]] = qaxis(X, t)
        R[byname[B["shin"+side]]] = qaxis(X, k)
        R[byname[B["foot"+side]]] = qaxis(X, a)
        R[byname[B["toe"+side]]] = qaxis(X, o)
    R[byname[B["root"]]] = root_rot(u)
    for key, lag, w in [("spine1", .03, .5), ("spine2", .05, .8),
                        ("spine3", .07, 1.1), ("chest", .09, 1.5)]:
        rx = LEAN/4+1.2*math.cos(4*math.pi*(u-0.20-lag))
        R[byname[B[key]]] = qmul(qaxis(X, rx), qaxis(Y, 0.05*w*at(THIGH, u-lag)))
    R[byname[B["neck"]]] = qmul(qaxis(X, -LEAN*.45-1.0*math.cos(4*math.pi*(u-.30))),
                                qaxis(Y, -0.06*at(THIGH, u-0.12)))
    R[byname[B["head"]]] = qaxis(X, -LEAN*.30-1.5*math.cos(4*math.pi*(u-0.34)))
    for side, sgn in (("A", +1), ("B", -1)):
        tl = at(THIGH, (u-0.05)+(0.0 if side == "A" else 0.5))
        sw = float(np.clip(-0.80*tl-4.0, -42, 34))
        drop = 72.0+5.0*math.sin(2*math.pi*(u-0.05)+(0 if side == "A" else math.pi))
        R[byname[B["clav"+side]]] = qmul(qaxis(Z, sgn*4.0), qaxis(X, -0.05*tl))
        R[byname[B["uarm"+side]]] = qmul(qaxis(X, sw), qaxis(Z, sgn*drop))
        R[byname[B["farm"+side]]] = qaxis(Y, sgn*float(np.clip(82.0-0.62*sw, 55, 110)))
        R[byname[B["hand"+side]]] = qmul(qaxis(Y, sgn*12.0), qaxis(X, -0.06*sw))
    for chains, sgn in ((FING_A, +1), (FING_B, -1)):
        for ch in chains:
            for j, bn in enumerate(ch): R[byname[bn]] = qaxis(Y, sgn*(40 if j else 28))
    for ch, sgn in zip(THUMB, (+1, -1)):
        for bn in ch: R[byname[bn]] = qaxis(Y, sgn*12)
    flare = [2, 6, 10, 12, 12, 10]; react = [3, 9, 14, 18, 21, 23]
    sway = [1.0, 2.2, 3.6, 5.0, 6.2, 7.0]
    for i, bn in enumerate(CAPE):
        lag = 0.085*i
        rx = flare[i]-react[i]*at(VROOT, u-lag)+0.35*react[i]*at(VROOT, u-lag*1.9)
        rz = sway[i]*math.sin(2*math.pi*(u-lag*1.25)+0.9)-0.05*sway[i]*at(THIGH, u-lag)
        R[byname[bn]] = qmul(qaxis(X, rx), qaxis(Z, rz))
    return R

print("\nvalidacao (folga da sola, metros):")
for f in range(0, 24):
    u = f/24
    a = ground(u, "A")+at(ROOTY, u); b = ground(u, "B")+at(ROOTY, u)
    tag = ("A" if a < 0.010 else "")+("B" if b < 0.010 else "")
    print(f"  u={u:4.2f}  A={a:6.3f}  B={b:6.3f}   {tag or 'VOO'}")

# ---------------------------------------------------------------- export
ANIM = sorted(world_rots(0.0).keys())
times = np.arange(NS+1)*(CYCLE/NS)
rot = {i: [] for i in ANIM}; tra = []
def loc(R):
    o = {}
    for i, rw in R.items():
        gp = Gq[parent[i]] if i in parent else np.array([0, 0, 0, 1.0])
        o[i] = qmul(qmul(qmul(qconj(gp), rw), gp), Lq[i])
    return o
for f in range(NS+1):
    u = (f % NS)/NS; lq = loc(world_rots(u))
    for i in ANIM:
        q = lq[i]/np.linalg.norm(lq[i])
        if rot[i] and np.dot(rot[i][-1], q) < 0: q = -q
        rot[i].append(q)
    tra.append(Lt[byname[B["root"]]]+np.array([at(ROOTX, u), at(ROOTY, u), 0.0]))

blob = bytearray(g.binary_blob())
def add_view(d):
    while len(blob) % 4: blob.append(0)
    o = len(blob); blob.extend(d)
    g.bufferViews.append(BufferView(buffer=0, byteOffset=o, byteLength=len(d)))
    return len(g.bufferViews)-1
def add_acc(a, typ):
    a = np.asarray(a, np.float32); bv = add_view(a.tobytes())
    g.accessors.append(Accessor(bufferView=bv, componentType=5126, count=len(a), type=typ,
                                min=a.min(0).tolist() if a.ndim > 1 else [float(a.min())],
                                max=a.max(0).tolist() if a.ndim > 1 else [float(a.max())]))
    return len(g.accessors)-1
t_acc = add_acc(times.astype(np.float32), "SCALAR"); sam, cha = [], []
def add(n, path, v, typ):
    va = add_acc(np.array(v, np.float32), typ)
    sam.append(AnimationSampler(input=t_acc, output=va, interpolation="LINEAR"))
    cha.append(AnimationChannel(sampler=len(sam)-1,
                                target=AnimationChannelTarget(node=n, path=path)))
for i in ANIM: add(i, "rotation", rot[i], "VEC4")
add(byname[B["root"]], "translation", tra, "VEC3")
g.animations = [Animation(name="Run", samplers=sam, channels=cha)]
g.set_binary_blob(bytes(blob)); g.buffers[0].byteLength = len(blob)
g.save(OUT)
print("\ngerado:", OUT, "| bones:", len(ANIM), "| chaves:", NS+1, "| %.2fs" % CYCLE)
