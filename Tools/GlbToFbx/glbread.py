"""
Leitor de GLB sem dependencia externa. Devolve o JSON e um acessor de buffers.

Existe porque o pygltflib nao esta instalado e instalar biblioteca na maquina de outra pessoa
para ler 12 campos de um JSON e desproporcional.
"""
import json, struct

COMP = {5120: ('b', 1), 5121: ('B', 1), 5122: ('h', 2),
        5123: ('H', 2), 5125: ('I', 4), 5126: ('f', 4)}
NUM = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}


def load(path):
    with open(path, 'rb') as f:
        data = f.read()
    magic, version, length = struct.unpack_from('<III', data, 0)
    assert magic == 0x46546C67, "nao e um GLB"

    pos, chunks = 12, {}
    while pos < length:
        clen, ctype = struct.unpack_from('<II', data, pos)
        pos += 8
        chunks[ctype] = data[pos:pos + clen]
        pos += clen

    g = json.loads(chunks[0x4E4F534A].decode('utf-8'))
    return g, chunks.get(0x004E4942, b'')


def accessor(g, buf, index):
    """Devolve uma lista de tuplas (ou de escalares para SCALAR)."""
    acc = g['accessors'][index]
    fmt, size = COMP[acc['componentType']]
    n = NUM[acc['type']]

    if 'bufferView' not in acc:                       # accessor esparso/zerado
        return [(0.0,) * n if n > 1 else 0.0] * acc['count']

    bv = g['bufferViews'][acc['bufferView']]
    base = bv.get('byteOffset', 0) + acc.get('byteOffset', 0)
    stride = bv.get('byteStride') or (size * n)

    out = []
    for i in range(acc['count']):
        v = struct.unpack_from('<' + fmt * n, buf, base + i * stride)
        out.append(v[0] if n == 1 else v)
    return out


def hierarchy(g):
    """pai[i] = indice do no pai, ou None."""
    parent = {}
    for i, node in enumerate(g.get('nodes', [])):
        for c in node.get('children', []):
            parent[c] = i
    return parent
