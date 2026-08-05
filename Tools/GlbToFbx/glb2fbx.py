"""
Converte um GLB riggado e animado em FBX ASCII que o Unity importa nativamente.

POR QUE ISTO EXISTE
    O Unity nao le .glb — nem nativamente, nem sem instalar um pacote de glTF. E mesmo com o
    pacote, todo o pipeline do projeto e ModelImporter (avatar, clipes, import settings), coisa
    que importador de glTF nao expoe. Adotar GLB significaria manter DUAS pipelines para a mesma
    coisa. Converter na entrada mantem uma so.

O QUE SAI
    <Nome>.fbx       malha + esqueleto + skin, sem animacao
    <Nome>_<Clipe>.fbx   so o esqueleto + curvas   (o clipe nao precisa da malha, e sem ela o
                         arquivo cai de ~12 MB para ~200 KB)

    Que e exatamente a convencao que ArtSetup/CharacterSetup ja esperam.

USO
    python glb2fbx.py entrada.glb PastaDeSaida Nome [SufixoDoClipe]
"""
import sys, os, math, struct
import glbread

# FBX conta tempo em unidades de 1/46186158000 de segundo.
KTIME = 46186158000

# Interpolacao linear com tangentes zeradas. O importador do Unity reamostra as curvas de
# qualquer jeito (resampleCurves), entao o que importa aqui e a densidade de chaves, nao o
# tipo de tangente — e o gerador ja entrega 48 chaves em 0,62 s (~77 Hz).
KEY_LINEAR = 24836


# ----------------------------------------------------------------------------------------------
# Matematica
# ----------------------------------------------------------------------------------------------

def quat_to_matrix(q):
    """Quaternion glTF (x,y,z,w) -> matriz 3x3, convencao de vetor-coluna (v' = M.v)."""
    x, y, z, w = q
    n = math.sqrt(x * x + y * y + z * z + w * w) or 1.0
    x, y, z, w = x / n, y / n, z / n, w / n
    return [
        [1 - 2 * (y * y + z * z), 2 * (x * y - z * w),     2 * (x * z + y * w)],
        [2 * (x * y + z * w),     1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
        [2 * (x * z - y * w),     2 * (y * z + x * w),     1 - 2 * (x * x + y * y)],
    ]


def matrix_to_euler_xyz(m):
    """
    Matriz -> Euler XYZ em GRAUS, na ordem do FBX (eEulerXYZ: X primeiro, depois Y, depois Z,
    ou seja M = Rz.Ry.Rx).

    Perto do travamento de gimbal (|sen(Y)| = 1) X e Z deixam de ser separaveis; ali o angulo
    inteiro vai para X, que e a escolha convencional e continua reproduzindo a MESMA rotacao.
    """
    sy = -m[2][0]
    sy = max(-1.0, min(1.0, sy))
    y = math.asin(sy)

    if abs(sy) < 0.999999:
        x = math.atan2(m[2][1], m[2][2])
        z = math.atan2(m[1][0], m[0][0])
    else:
        x = math.atan2(-m[1][2], m[1][1])
        z = 0.0

    return [math.degrees(x), math.degrees(y), math.degrees(z)]


def euler_xyz_to_matrix(e):
    """Inversa de matrix_to_euler_xyz. Existe para a auto-verificacao, nao para exportar."""
    x, y, z = (math.radians(v) for v in e)
    cx, sx, cy, sy, cz, sz = (math.cos(x), math.sin(x), math.cos(y),
                              math.sin(y), math.cos(z), math.sin(z))
    return [
        [cz * cy, cz * sy * sx - sz * cx, cz * sy * cx + sz * sx],
        [sz * cy, sz * sy * sx + cz * cx, sz * sy * cx - cz * sx],
        [-sy,     cy * sx,                cy * cx],
    ]


def unwrap(previous, current):
    """
    Aproxima cada angulo do valor anterior somando voltas inteiras.

    Sem isto uma curva que passa de 179 para -179 graus e lida como um giro de 358 graus no
    sentido errado — o membro roda ao contrario num unico quadro.
    """
    out = []
    for p, c in zip(previous, current):
        while c - p > 180.0:
            c -= 360.0
        while c - p < -180.0:
            c += 360.0
        out.append(c)
    return out


def mat_mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]


def mat_apply(m, v):
    return [sum(m[i][k] * v[k] for k in range(3)) for i in range(3)]


# ----------------------------------------------------------------------------------------------
# Escrita
# ----------------------------------------------------------------------------------------------

class Fbx:
    def __init__(self):
        self.out = []
        self.depth = 0
        self._next_id = 1000000

    def id(self):
        self._next_id += 1
        return self._next_id

    def line(self, text):
        self.out.append("    " * self.depth + text)

    def open(self, text):
        self.line(text + " {")
        self.depth += 1

    def close(self):
        self.depth -= 1
        self.line("}")

    def array(self, name, values, per_line=12, fmt="%.6f"):
        if not values:
            self.line(f"{name}: *0 {{")
            self.depth += 1
            self.line("a: ")
            self.depth -= 1
            self.line("}")
            return

        self.line(f"{name}: *{len(values)} {{")
        self.depth += 1
        pad = "    " * self.depth
        chunks = []
        for i in range(0, len(values), per_line):
            chunks.append(",".join(fmt % v if isinstance(v, float) else str(v)
                                   for v in values[i:i + per_line]))
        self.out.append(pad + "a: " + ("\n" + pad).join(chunks))
        self.depth -= 1
        self.line("}")

    def text(self):
        return "\n".join(self.out) + "\n"


def header(f, creator):
    # A PRIMEIRA LINHA identifica o arquivo. O leitor da Autodesk reconhece o formato binario
    # pela assinatura "Kaydara FBX Binary" e o ASCII por este comentario — sem ele o import
    # falha com "Couldn't read file" e nenhuma pista de qual campo estaria errado.
    f.line("; FBX 7.4.0 project file")
    f.line("; " + "-" * 68)
    f.line("")

    f.open("FBXHeaderExtension:")
    f.line("FBXHeaderVersion: 1003")
    f.line("FBXVersion: 7400")
    f.open("CreationTimeStamp:")
    for k, v in (("Version", 1000), ("Year", 2026), ("Month", 1), ("Day", 1),
                 ("Hour", 0), ("Minute", 0), ("Second", 0), ("Millisecond", 0)):
        f.line(f"{k}: {v}")
    f.close()
    f.line(f'Creator: "{creator}"')
    f.close()
    f.line("")

    f.open("GlobalSettings:")
    f.line("Version: 1000")
    f.open("Properties70:")
    # glTF: Y para cima, +Z para o observador, destro. E exatamente o que estes seis campos
    # dizem — sem eles o Unity assume Z-up e o personagem entra deitado.
    for name, value in (("UpAxis", 1), ("UpAxisSign", 1), ("FrontAxis", 2), ("FrontAxisSign", 1),
                        ("CoordAxis", 0), ("CoordAxisSign", 1), ("OriginalUpAxis", 1),
                        ("OriginalUpAxisSign", 1)):
        f.line(f'P: "{name}", "int", "Integer", "",{value}')
    # As coordenadas saem em METROS; 100 cm por unidade e o que faz o Unity importar 1,70 de
    # altura em vez de 0,017.
    f.line('P: "UnitScaleFactor", "double", "Number", "",100')
    f.line('P: "OriginalUnitScaleFactor", "double", "Number", "",100')
    f.line('P: "TimeMode", "enum", "", "",6')
    f.close()
    f.close()
    f.line("")


def definitions(f, counts):
    f.open("Definitions:")
    f.line("Version: 100")
    f.line(f"Count: {sum(counts.values())}")
    for kind, n in counts.items():
        f.open(f'ObjectType: "{kind}"')
        f.line(f"Count: {n}")
        f.close()
    f.close()
    f.line("")


def model_node(f, oid, name, kind, translation, euler, scale):
    f.open(f'Model: {oid}, "Model::{name}", "{kind}"')
    f.line("Version: 232")
    f.open("Properties70:")
    f.line('P: "RotationActive", "bool", "", "",1')
    f.line('P: "InheritType", "enum", "", "",0')
    f.line('P: "ScalingMax", "Vector3D", "Vector", "",0,0,0')
    f.line('P: "DefaultAttributeIndex", "int", "Integer", "",0')
    # A flag "A" marca a propriedade como animavel. Sem ela a curva conecta e nao toca.
    f.line('P: "Lcl Translation", "Lcl Translation", "", "A",%.6f,%.6f,%.6f' % tuple(translation))
    f.line('P: "Lcl Rotation", "Lcl Rotation", "", "A",%.6f,%.6f,%.6f' % tuple(euler))
    f.line('P: "Lcl Scaling", "Lcl Scaling", "", "A",%.6f,%.6f,%.6f' % tuple(scale))
    f.close()
    f.line("Shading: T")
    f.line('Culling: "CullingOff"')
    f.close()


# ----------------------------------------------------------------------------------------------
# Leitura do GLB
# ----------------------------------------------------------------------------------------------

class Rig:
    """O que interessa do GLB, ja normalizado."""

    def __init__(self, path):
        self.g, self.buf = glbread.load(path)
        self.parent = glbread.hierarchy(self.g)
        self.nodes = self.g["nodes"]

        self.skin = self.g["skins"][0]
        self.joints = self.skin["joints"]
        self.ibm = glbread.accessor(self.g, self.buf, self.skin["inverseBindMatrices"])

        # Todo no que participa da hierarquia do esqueleto (juntas + ancestrais delas).
        needed = set()
        for j in self.joints:
            k = j
            while k is not None:
                needed.add(k)
                k = self.parent.get(k)
        self.skeleton = sorted(needed)

        self.animation = self.g["animations"][0] if self.g.get("animations") else None

        self.mesh_node = next(i for i, n in enumerate(self.nodes) if "mesh" in n)

    def name(self, i):
        return self.nodes[i].get("name") or f"node{i}"

    def trs(self, i):
        n = self.nodes[i]
        t = list(n.get("translation", [0.0, 0.0, 0.0]))
        q = list(n.get("rotation", [0.0, 0.0, 0.0, 1.0]))
        s = list(n.get("scale", [1.0, 1.0, 1.0]))
        return t, q, s

    def curves(self):
        """{no: {'rotation': (tempos, [quat]), 'translation': (tempos, [vec3])}}"""
        result = {}
        if not self.animation:
            return result

        for channel in self.animation["channels"]:
            target = channel["target"]
            path = target["path"]
            if path not in ("rotation", "translation", "scale"):
                continue

            sampler = self.animation["samplers"][channel["sampler"]]
            times = glbread.accessor(self.g, self.buf, sampler["input"])
            values = glbread.accessor(self.g, self.buf, sampler["output"])
            result.setdefault(target["node"], {})[path] = (times, values)

        return result

    def world_pose(self, node, sample):
        """
        Posicao do no no espaco do modelo, num instante. Serve de GABARITO: e contra estes
        numeros que a importacao no Unity e conferida.
        """
        chain = []
        k = node
        while k is not None:
            chain.append(k)
            k = self.parent.get(k)

        position = [0.0, 0.0, 0.0]
        rotation = [[1, 0, 0], [0, 1, 0], [0, 0, 1]]

        for k in reversed(chain):
            t, q, s = self.trs(k)
            anim = sample.get(k, {})
            if "translation" in anim:
                t = list(anim["translation"])
            if "rotation" in anim:
                q = list(anim["rotation"])

            position = [position[i] + mat_apply(rotation, t)[i] for i in range(3)]
            rotation = mat_mul(rotation, quat_to_matrix(q))

        return position


def sample_at(curves, time):
    """Amostra LINEAR das curvas num instante — o mesmo que o glTF declara."""
    out = {}
    for node, paths in curves.items():
        entry = {}
        for path, (times, values) in paths.items():
            if time <= times[0]:
                entry[path] = list(values[0])
                continue
            if time >= times[-1]:
                entry[path] = list(values[-1])
                continue
            for i in range(1, len(times)):
                if times[i] >= time:
                    u = (time - times[i - 1]) / (times[i] - times[i - 1])
                    a, b = values[i - 1], values[i]
                    entry[path] = [a[k] + (b[k] - a[k]) * u for k in range(len(a))]
                    break
        out[node] = entry
    return out


# ----------------------------------------------------------------------------------------------
# Malha
# ----------------------------------------------------------------------------------------------

def write_mesh_fbx(rig, path, name, max_influences=4):
    f = Fbx()
    header(f, "Destiny Together glb2fbx")

    prim = rig.g["meshes"][0]["primitives"][0]
    attrs = prim["attributes"]

    positions = glbread.accessor(rig.g, rig.buf, attrs["POSITION"])
    normals = glbread.accessor(rig.g, rig.buf, attrs["NORMAL"])
    uvs = glbread.accessor(rig.g, rig.buf, attrs["TEXCOORD_0"]) if "TEXCOORD_0" in attrs else None
    indices = glbread.accessor(rig.g, rig.buf, prim["indices"])

    # Pesos: o GLB traz ate 12 influencias por vertice (JOINTS_0/1/2). O Unity usa 4
    # (maxBonesPerVertex). Podar aqui em vez de deixar o importador podar mantem o arquivo tres
    # vezes menor E torna o resultado deterministico — quem escolhe as quatro somos nos.
    influences = [[] for _ in positions]
    for set_index in range(3):
        jk, wk = f"JOINTS_{set_index}", f"WEIGHTS_{set_index}"
        if jk not in attrs or wk not in attrs:
            continue
        joints = glbread.accessor(rig.g, rig.buf, attrs[jk])
        weights = glbread.accessor(rig.g, rig.buf, attrs[wk])
        for v in range(len(positions)):
            for c in range(4):
                w = weights[v][c]
                if w > 1e-5:
                    influences[v].append((joints[v][c], w))

    clusters = {j: ([], []) for j in range(len(rig.joints))}
    for v, items in enumerate(influences):
        items.sort(key=lambda p: -p[1])
        items = items[:max_influences]
        total = sum(w for _, w in items) or 1.0
        for joint, w in items:
            clusters[joint][0].append(v)
            clusters[joint][1].append(w / total)

    counts = {"GlobalSettings": 1, "Model": len(rig.skeleton) + 1, "Geometry": 1,
              "Material": 1, "Deformer": 1 + len(rig.joints),
              "NodeAttribute": len(rig.skeleton), "Pose": 1}
    definitions(f, counts)

    f.open("Objects:")

    geometry_id = f.id()
    f.open(f'Geometry: {geometry_id}, "Geometry::{name}", "Mesh"')
    f.line("GeometryVersion: 124")

    flat = []
    for p in positions:
        flat.extend(p)
    f.array("Vertices", flat)

    poly = []
    for i in range(0, len(indices), 3):
        poly.extend([indices[i], indices[i + 1], -(indices[i + 2] + 1)])
    f.array("PolygonVertexIndex", poly, per_line=24)

    f.open("LayerElementNormal: 0")
    f.line("Version: 101")
    f.line('Name: ""')
    f.line('MappingInformationType: "ByVertice"')
    f.line('ReferenceInformationType: "Direct"')
    flat = []
    for n in normals:
        flat.extend(n)
    f.array("Normals", flat)
    f.close()

    if uvs:
        f.open("LayerElementUV: 0")
        f.line("Version: 101")
        f.line('Name: "UVMap"')
        f.line('MappingInformationType: "ByPolygonVertex"')
        f.line('ReferenceInformationType: "IndexToDirect"')
        flat = []
        for u in uvs:
            # glTF conta V de cima para baixo; FBX e Unity, de baixo para cima.
            flat.extend([u[0], 1.0 - u[1]])
        f.array("UV", flat)
        f.array("UVIndex", list(indices), per_line=24)
        f.close()

    f.open("LayerElementMaterial: 0")
    f.line("Version: 101")
    f.line('Name: ""')
    f.line('MappingInformationType: "AllSame"')
    f.line('ReferenceInformationType: "IndexToDirect"')
    f.array("Materials", [0])
    f.close()

    f.open("Layer: 0")
    f.line("Version: 100")
    for kind in ("LayerElementNormal", "LayerElementUV", "LayerElementMaterial"):
        if kind == "LayerElementUV" and not uvs:
            continue
        f.open("LayerElement:")
        f.line(f'Type: "{kind}"')
        f.line("TypedIndex: 0")
        f.close()
    f.close()
    f.close()

    model_ids, attribute_ids = write_skeleton(f, rig)

    mesh_model_id = f.id()
    mesh_t, mesh_q, mesh_s = rig.trs(rig.mesh_node)
    model_node(f, mesh_model_id, name, "Mesh",
               mesh_t, matrix_to_euler_xyz(quat_to_matrix(mesh_q)), mesh_s)

    material_id = f.id()
    f.open(f'Material: {material_id}, "Material::{name}", ""')
    f.line("Version: 102")
    f.line('ShadingModel: "phong"')
    f.line("MultiLayer: 0")
    f.open("Properties70:")
    f.line('P: "DiffuseColor", "Color", "", "A",1,1,1')
    f.close()
    f.close()

    skin_id = f.id()
    f.open(f'Deformer: {skin_id}, "Deformer::Skin", "Skin"')
    f.line("Version: 101")
    f.line("Link_DeformAcuracy: 50")
    f.close()

    cluster_ids = []
    for index, joint in enumerate(rig.joints):
        cid = f.id()
        cluster_ids.append(cid)
        vertices, weights = clusters[index]

        f.open(f'Deformer: {cid}, "SubDeformer::Cluster {rig.name(joint)}", "Cluster"')
        f.line("Version: 100")
        f.line('UserData: "", ""')
        f.array("Indexes", vertices, per_line=24)
        f.array("Weights", weights, per_line=12)

        # O glTF guarda a inversa da pose de ligacao (malha -> osso). E exatamente o que o FBX
        # chama de "Transform"; "TransformLink" e o caminho de volta (osso -> malha). As duas
        # convencoes gravam o mesmo vetor de 16 numeros, porque coluna-maior transposto E
        # linha-maior — por isso os floats passam direto, sem reordenar.
        f.array("Transform", list(rig.ibm[index]))
        f.array("TransformLink", invert_affine(rig.ibm[index]))
        f.close()

    pose_id = f.id()
    f.open(f'Pose: {pose_id}, "Pose::BIND_POSES", "BindPose"')
    f.line('Type: "BindPose"')
    f.line("Version: 100")
    f.line(f"NbPoseNodes: {len(rig.joints) + 1}")
    for index, joint in enumerate(rig.joints):
        f.open("PoseNode:")
        f.line(f"Node: {model_ids[joint]}")
        f.array("Matrix", invert_affine(rig.ibm[index]))
        f.close()
    f.open("PoseNode:")
    f.line(f"Node: {mesh_model_id}")
    f.array("Matrix", [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1.0])
    f.close()
    f.close()

    f.close()

    f.open("Connections:")
    write_skeleton_links(f, rig, model_ids, attribute_ids)
    # A malha entra SOB a armadura, como no GLB. Nao e detalhe de arrumacao: se ela ficasse na
    # raiz, o FBX da malha teria DUAS raizes e o do clipe uma so — e o Unity ancora os caminhos de
    # curva de forma diferente nos dois casos (com uma raiz, ela vira o proprio objeto raiz). O
    # resultado era um clipe cujos caminhos eram "Bone_000" contra uma hierarquia
    # "UniRigArmature/Bone_000": 47 curvas, 47 sem correspondencia, nenhum erro no console.
    mesh_parent = rig.parent.get(rig.mesh_node)
    f.line(f'C: "OO",{mesh_model_id},{model_ids.get(mesh_parent, 0)}')
    f.line(f'C: "OO",{geometry_id},{mesh_model_id}')
    f.line(f'C: "OO",{material_id},{mesh_model_id}')
    f.line(f'C: "OO",{skin_id},{geometry_id}')
    for index, cid in enumerate(cluster_ids):
        f.line(f'C: "OO",{cid},{skin_id}')
        f.line(f'C: "OO",{model_ids[rig.joints[index]]},{cid}')
    f.close()

    with open(path, "w", encoding="utf-8") as handle:
        handle.write(f.text())

    return len(positions), len(indices) // 3


def invert_affine(m):
    """
    Inversa de uma matriz afim 4x4 no layout do glTF (coluna-maior, translacao em 12..14).

    Assume rotacao+escala uniforme sem cisalhamento, que e o caso de uma pose de ligacao.
    """
    r = [[m[0], m[4], m[8]], [m[1], m[5], m[9]], [m[2], m[6], m[10]]]
    t = [m[12], m[13], m[14]]

    scale2 = sum(r[i][0] * r[i][0] for i in range(3)) or 1.0
    inv = [[r[j][i] / scale2 for j in range(3)] for i in range(3)]
    it = [-sum(inv[i][k] * t[k] for k in range(3)) for i in range(3)]

    return [inv[0][0], inv[1][0], inv[2][0], 0.0,
            inv[0][1], inv[1][1], inv[2][1], 0.0,
            inv[0][2], inv[1][2], inv[2][2], 0.0,
            it[0], it[1], it[2], 1.0]


def write_skeleton(f, rig):
    model_ids, attribute_ids = {}, {}

    for node in rig.skeleton:
        t, q, s = rig.trs(node)
        euler = matrix_to_euler_xyz(quat_to_matrix(q))

        oid = f.id()
        model_ids[node] = oid
        # A raiz da armadura e um Null; osso e LimbNode. O Unity usa isto para saber o que e
        # esqueleto — sem o tipo certo, os ossos entram como transforms comuns.
        kind = "LimbNode" if node in rig.joints else "Null"
        model_node(f, oid, rig.name(node), kind, t, euler, s)

        aid = f.id()
        attribute_ids[node] = aid
        f.open(f'NodeAttribute: {aid}, "NodeAttribute::", "{kind}"')
        f.open("Properties70:")
        f.line('P: "Size", "double", "Number", "",1')
        f.close()
        f.line(f'TypeFlags: "{"Skeleton" if kind == "LimbNode" else "Null"}"')
        f.close()

    return model_ids, attribute_ids


def write_skeleton_links(f, rig, model_ids, attribute_ids):
    for node in rig.skeleton:
        parent = rig.parent.get(node)
        target = model_ids[parent] if parent in model_ids else 0
        f.line(f'C: "OO",{model_ids[node]},{target}')
        f.line(f'C: "OO",{attribute_ids[node]},{model_ids[node]}')


# ----------------------------------------------------------------------------------------------
# Clipe
# ----------------------------------------------------------------------------------------------

def write_clip_fbx(rig, path, clip_name):
    """
    So o esqueleto e as curvas — sem malha.

    O clipe liga por CAMINHO de transform, entao basta a hierarquia bater com a do personagem.
    Levar a malha junto dobraria 12 MB por clipe sem mudar um quadro.
    """
    f = Fbx()
    header(f, "Destiny Together glb2fbx")

    curves = rig.curves()
    animated = sorted(curves.keys())

    curve_count = 0
    for paths in curves.values():
        curve_count += 3 * len(paths)

    counts = {"GlobalSettings": 1, "Model": len(rig.skeleton),
              "NodeAttribute": len(rig.skeleton), "AnimationStack": 1, "AnimationLayer": 1,
              "AnimationCurveNode": sum(len(p) for p in curves.values()),
              "AnimationCurve": curve_count}
    definitions(f, counts)

    duration = 0.0
    for paths in curves.values():
        for times, _ in paths.values():
            duration = max(duration, times[-1])

    f.open("Objects:")
    model_ids, attribute_ids = write_skeleton(f, rig)

    stack_id = f.id()
    f.open(f'AnimationStack: {stack_id}, "AnimStack::{clip_name}", ""')
    f.open("Properties70:")
    f.line('P: "LocalStart", "KTime", "Time", "",0')
    f.line(f'P: "LocalStop", "KTime", "Time", "",{int(duration * KTIME)}')
    f.line('P: "ReferenceStart", "KTime", "Time", "",0')
    f.line(f'P: "ReferenceStop", "KTime", "Time", "",{int(duration * KTIME)}')
    f.close()
    f.close()

    layer_id = f.id()
    f.open(f'AnimationLayer: {layer_id}, "AnimLayer::BaseLayer", ""')
    f.close()

    links = []
    worst_jump = 0.0

    for node in animated:
        for channel_path, (times, values) in sorted(curves[node].items()):
            if channel_path == "rotation":
                property_name, channels = "Lcl Rotation", []
                previous = None
                for q in values:
                    euler = matrix_to_euler_xyz(quat_to_matrix(q))
                    if previous is not None:
                        # Desenrolar contra a chave ANTERIOR, e medir o salto so entre chaves
                        # reais: comparar a primeira com um zero inventado acusaria uma pulada de
                        # 178 graus que nunca existiu.
                        euler = unwrap(previous, euler)
                        worst_jump = max(worst_jump,
                                         max(abs(a - b) for a, b in zip(euler, previous)))
                    previous = euler
                    channels.append(euler)
            elif channel_path == "translation":
                property_name = "Lcl Translation"
                channels = [list(v) for v in values]
            else:
                property_name = "Lcl Scaling"
                channels = [list(v) for v in values]

            node_id = f.id()
            f.open(f'AnimationCurveNode: {node_id}, "AnimCurveNode::{property_name[4]}", ""')
            f.open("Properties70:")
            for axis, index in (("X", 0), ("Y", 1), ("Z", 2)):
                f.line(f'P: "d|{axis}", "Number", "", "A",{channels[0][index]:.6f}')
            f.close()
            f.close()

            key_times = [int(t * KTIME) for t in times]
            curve_ids = []
            for index in range(3):
                cid = f.id()
                curve_ids.append(cid)
                f.open(f'AnimationCurve: {cid}, "AnimCurve::", ""')
                f.line("Default: %.6f" % channels[0][index])
                f.line("KeyVer: 4008")
                f.array("KeyTime", key_times, per_line=8)
                f.array("KeyValueFloat", [c[index] for c in channels], per_line=8)
                f.array("KeyAttrFlags", [KEY_LINEAR])
                f.array("KeyAttrDataFloat", [0.0, 0.0, 0.0, 0.0])
                f.array("KeyAttrRefCount", [len(key_times)])
                f.close()

            links.append((node_id, model_ids[node], property_name, curve_ids))

    f.close()

    f.open("Connections:")
    write_skeleton_links(f, rig, model_ids, attribute_ids)
    f.line(f'C: "OO",{layer_id},{stack_id}')
    for node_id, model_id, property_name, curve_ids in links:
        f.line(f'C: "OO",{node_id},{layer_id}')
        f.line(f'C: "OP",{node_id},{model_id}, "{property_name}"')
        for axis, cid in zip("XYZ", curve_ids):
            f.line(f'C: "OP",{cid},{node_id}, "d|{axis}"')
    f.close()

    with open(path, "w", encoding="utf-8") as handle:
        handle.write(f.text())

    return duration, len(animated), worst_jump


# ----------------------------------------------------------------------------------------------

def self_check(rig):
    """Euler ida e volta. Se esta conversao estiver errada, TODO o resto esta."""
    worst = 0.0
    for node in rig.skeleton:
        _, q, _ = rig.trs(node)
        m = quat_to_matrix(q)
        back = euler_xyz_to_matrix(matrix_to_euler_xyz(m))
        worst = max(worst, max(abs(m[i][j] - back[i][j]) for i in range(3) for j in range(3)))

    if rig.animation:
        for _, paths in rig.curves().items():
            if "rotation" not in paths:
                continue
            for q in paths["rotation"][1]:
                m = quat_to_matrix(q)
                back = euler_xyz_to_matrix(matrix_to_euler_xyz(m))
                worst = max(worst, max(abs(m[i][j] - back[i][j]) for i in range(3) for j in range(3)))

    return worst


def main():
    source, folder, name = sys.argv[1], sys.argv[2], sys.argv[3]
    clip = sys.argv[4] if len(sys.argv) > 4 else None

    rig = Rig(source)
    os.makedirs(folder, exist_ok=True)

    error = self_check(rig)
    print(f"  auto-teste Euler<->quaternion: erro maximo {error:.2e}"
          f"  {'ok' if error < 1e-5 else '<<< FALHOU'}")

    mesh_path = os.path.join(folder, f"{name}.fbx")
    vertices, triangles = write_mesh_fbx(rig, mesh_path, name)
    print(f"  malha    {os.path.getsize(mesh_path):>12,}  {name}.fbx  "
          f"({vertices:,} vertices, {triangles:,} triangulos, {len(rig.joints)} ossos)")

    if clip and rig.animation:
        clip_path = os.path.join(folder, f"{name}_{clip}.fbx")
        duration, nodes, jump = write_clip_fbx(rig, clip_path, clip)
        print(f"  clipe    {os.path.getsize(clip_path):>12,}  {name}_{clip}.fbx  "
              f"({duration:.2f}s, {nodes} nos animados, maior salto {jump:.1f} graus)")

    # Gabarito: onde alguns ossos estao no espaco do modelo, para conferir contra o Unity.
    if rig.animation:
        print("\n  GABARITO (posicao no espaco do modelo, metros)")
        interesting = [j for j in rig.joints
                       if rig.name(j) in ("Bone_000", "Bone_014", "Bone_009", "Bone_021", "Bone_018")]
        for t in (0.0, 0.155, 0.31):
            sample = sample_at(rig.curves(), t)
            parts = []
            for j in interesting:
                p = rig.world_pose(j, sample)
                parts.append(f"{rig.name(j)}=({p[0]:+.3f},{p[1]:+.3f},{p[2]:+.3f})")
            print(f"    t={t:.3f}  " + "  ".join(parts))


if __name__ == "__main__":
    main()
