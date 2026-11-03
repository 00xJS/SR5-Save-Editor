"""Standalone decoder for Saints Row (2022) saves, written separately from Core.cs.

The tests run it on the files the editor writes, so a bug in the C# serializer
can't hide behind the C# parser. See FORMAT.md for the layout.

    python -I tools/sr5save.py check FILE.sf_pc [FILE...]   parse strictly, print a summary
    python -I tools/sr5save.py dump FILE.sf_pc              print every record and value
"""
import glob
import os
import struct
import sys

MAGIC_DATA = 0xE4C7BBD7
MAGIC_SCHEMA = 0xAE1F7795
OP_RECORD, OP_STRUCT, OP_STRUCT_END, OP_ARRAY, OP_ARRAY_END, OP_BLOB, OP_LONG = 0x01, 0x10, 0x11, 0x12, 0x13, 0x15, 0x0A
OP_STRING2 = 0x0F  # a string held in an array, seen in the game's tables
SCALAR_OPS = {0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x0B, 0x19}  # 0x19, a resource reference: only in the game's tables
OP_STRING = 0x0E


def u32(b, o):
    return struct.unpack_from('<I', b, o)[0]


def crc(s):
    if isinstance(s, str):
        s = s.encode('latin1')
    c = 0
    for x in s.lower():
        c ^= x
        for _ in range(8):
            c = (c >> 1) ^ (0xEDB88320 if c & 1 else 0)
    return c


def align8(o):
    return (o + 7) & ~7


def load_names():
    names = {}
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'data', 'names.txt')
    if os.path.exists(path):
        for line in open(path, encoding='utf8'):
            parts = line.rstrip('\n').split('\t')
            if len(parts) == 2:
                names[int(parts[0], 16)] = parts[1]
    return names


def parse_schema(b, o):
    assert u32(b, o) == MAGIC_SCHEMA, 'schema magic at %#x' % o
    o += 16
    o += 1  # tag byte
    n = u32(b, o)
    o += 4
    out = []
    for _ in range(n):
        h, _z, nf, size, al = struct.unpack_from('<5I', b, o)
        o += 20
        fields = []
        for _ in range(nf):
            stored = b[o]   # 0 in the game's tables for a field that has no offset; saves always have 1
            o += 1
            fh, _z, ty, bits, ref = struct.unpack_from('<5I', b, o)
            o += 20
            off = 0
            if stored:
                off = u32(b, o)
                o += 8
            fields.append(dict(hash=fh, type=ty, bits=bits, ref=ref, off=off))
        o += 3
        out.append(dict(hash=h, size=size, align=al, fields=fields))
    return out, o


def parse_records(b, start, end, schema):
    records = []
    pos = start
    while pos < end:
        v, t = struct.unpack_from('<II', b, pos)
        assert t >> 24 == OP_RECORD, 'expected a record at %#x, got op %#x' % (pos, t >> 24)
        rec_end = pos + v
        assert rec_end <= end, 'record at %#x overruns its block' % pos
        idx = (t >> 12) & 0xFFF
        assert idx < len(schema), 'record at %#x uses struct %d of %d' % (pos, idx, len(schema))
        key = u32(b, pos + 8)
        assert not b[pos + 12:pos + 24].strip(b'\0'), 'reserved bytes set at %#x' % pos
        ln = u32(b, pos + 24)
        name = b[pos + 28:pos + 27 + ln].decode('latin1') if ln else ''
        p = align8(pos + 28 + ln)
        node = dict(kind='rec', pos=pos, key=key, name=name, struct=schema[idx], children=[])
        stack = [node]
        while p < rec_end:
            v, t = struct.unpack_from('<II', b, p)
            op, f, low = t >> 24, (t >> 12) & 0xFFF, t & 0xFFF
            cur = stack[-1]
            if op == OP_STRUCT:
                assert v < len(schema), 'struct %d of %d at %#x' % (v, len(schema), p)
                n = dict(kind='struct', pos=p, f=f, struct=schema[v], children=[])
                cur['children'].append(n)
                stack.append(n)
                p += 8
            elif op == OP_ARRAY:
                n = dict(kind='array', pos=p, f=f, count=low, elem=v, children=[])
                cur['children'].append(n)
                stack.append(n)
                p += 8
            elif op in (OP_STRUCT_END, OP_ARRAY_END):
                assert len(stack) > 1, 'unbalanced end at %#x' % p
                ended = stack.pop()
                if op == OP_ARRAY_END:
                    assert ended['kind'] == 'array', 'array end for a struct at %#x' % p
                    assert ended['count'] == len(ended['children']), 'array count at %#x' % ended['pos']
                else:
                    assert ended['kind'] == 'struct', 'struct end for an array at %#x' % p
                p += 8
            elif op in (OP_BLOB, OP_STRING, OP_STRING2):
                cur['children'].append(dict(kind='blob', pos=p, f=f, len=v, data=b[p + 8:p + 8 + v]))
                p = align8(p + 8 + v)
            elif op in (OP_LONG, 0x0C):  # 0x0C, a double: only in the game's tables
                cur['children'].append(dict(kind='val', pos=p, f=f, op=op, v=struct.unpack_from('<Q', b, p + 8)[0]))
                p += 16
            elif op in SCALAR_OPS:
                cur['children'].append(dict(kind='val', pos=p, f=f, op=op, v=v))
                p += 8
            else:
                raise AssertionError('unknown op %#x at %#x' % (op, p))
        assert p == rec_end, 'record at %#x ended at %#x, expected %#x' % (pos, p, rec_end)
        assert len(stack) == 1, 'unclosed struct in record at %#x' % pos
        records.append(node)
        pos = rec_end
    assert pos == end, 'records ended at %#x, expected %#x' % (pos, end)
    return records


def parse_blocks(b):
    assert b[:4] == b'evas', 'not a save'
    n = u32(b, 8)
    o = 12
    out = []
    for _ in range(n):
        h, s = u32(b, o), u32(b, o + 4)
        p = o + 8
        assert u32(b, p) == MAGIC_DATA, 'data magic at %#x' % p
        ds, count = u32(b, p + 8), u32(b, p + 12)
        end = p + 8 + ds
        schema, se = parse_schema(b, end)
        assert not b[se:p + s].strip(b'\0'), 'non-zero padding after schema at %#x' % se
        assert (p + s) % 0x40 == 0, 'block at %#x does not end on 0x40' % o
        start = p + 16 if ds == 8 else align8(p + 16)
        out.append(dict(hash=h, pos=p, size=s, count=count, schema=schema, records=parse_records(b, start, end, schema)))
        o = p + s
    assert not b[o:].strip(b'\0'), 'data after the last block at %#x' % o
    return out, o


def fmt(c, names):
    op, v = c['op'], c['v']
    if op == 0x0B:
        return '%g' % struct.unpack('<f', struct.pack('<I', v))[0]
    if op == 0x07:
        return str(struct.unpack('<i', struct.pack('<I', v))[0])
    if op == 0x0A:
        return '0x%016x' % v
    if op == 0x08 and v >= 0x100000:
        return '0x%08x' % v + (' = ' + names[v] if v in names else '')
    return str(v)


def dump_children(node, struct_def, indent, names, out):
    for c in node['children']:
        f = struct_def['fields'][c['f']] if struct_def and c['f'] < len(struct_def['fields']) else None
        label = names.get(f['hash'], '%08x' % f['hash']) if f else '?'
        pad = ' ' * indent
        if c['kind'] == 'val':
            out.append('%s%s: %s' % (pad, label, fmt(c, names)))
        elif c['kind'] == 'blob':
            out.append('%s%s: blob of %d bytes' % (pad, label, c['len']))
        elif c['kind'] == 'struct':
            out.append('%s%s:' % (pad, label))
            dump_children(c, c['struct'], indent + 2, names, out)
        else:
            out.append('%s%s: [%d]' % (pad, label, c['count']))
            for i, e in enumerate(c['children']):
                if e['kind'] == 'val':
                    out.append('%s  [%d] %s' % (pad, i, fmt(e, names)))
                elif e['kind'] == 'struct':
                    out.append('%s  [%d]:' % (pad, i))
                    dump_children(e, e['struct'], indent + 4, names, out)


def main(argv):
    if len(argv) < 3 or argv[1] not in ('check', 'dump'):
        print(__doc__)
        return 2
    names = load_names()
    bad = 0
    # cmd.exe leaves wildcards for the program to expand.
    paths = [p for a in argv[2:] for p in (glob.glob(a) or [a])]
    for path in paths:
        b = open(path, 'rb').read()
        try:
            blocks, end = parse_blocks(b)
        except (AssertionError, struct.error, IndexError) as e:
            print('FAIL %s: %s' % (os.path.basename(path), e))
            bad += 1
            continue
        if argv[1] == 'check':
            print('ok   %s: %d blocks, %d records, data ends at %#x of %#x' % (
                os.path.basename(path), len(blocks), sum(len(x['records']) for x in blocks), end, len(b)))
            continue
        out = []
        for x in blocks:
            out.append('=== %s (%08x) count=%d records=%d' % (names.get(x['hash'], '?'), x['hash'], x['count'], len(x['records'])))
            for r in x['records']:
                out.append('- %s <%s>' % (r['name'] or names.get(r['key'], '%08x' % r['key']),
                                          names.get(r['struct']['hash'], '%08x' % r['struct']['hash'])))
                dump_children(r, r['struct'], 2, names, out)
        print('\n'.join(out))
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
