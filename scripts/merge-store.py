#!/usr/bin/env python3
"""Merges chest stores from several sidecar files into one, repacking grid positions.

Used to recover contents from a .old backup after a bad save. Later files win on
duplicate item identity only in the sense that all stacks are kept - nothing is dropped.
"""
import struct, io, sys, os

def r7(b):
    n = sh = 0
    while True:
        byte = b.read(1)[0]
        n |= (byte & 0x7F) << sh
        if not (byte & 0x80): break
        sh += 7
    return b.read(n).decode('utf-8')

def w7(b, s):
    data = s.encode('utf-8'); n = len(data)
    while True:
        byte = n & 0x7F; n >>= 7
        b.write(bytes([byte | (0x80 if n else 0)]))
        if not n: break
    b.write(data)

def read_item(p):
    it = {}
    it['name']   = r7(p)
    it['stack']  = struct.unpack('<i', p.read(4))[0]
    it['dur']    = struct.unpack('<f', p.read(4))[0]
    it['gx']     = struct.unpack('<i', p.read(4))[0]
    it['gy']     = struct.unpack('<i', p.read(4))[0]
    it['equip']  = p.read(1)
    it['qual']   = struct.unpack('<i', p.read(4))[0]
    it['var']    = struct.unpack('<i', p.read(4))[0]
    it['crafter']= p.read(8)
    it['cname']  = r7(p)
    cd = struct.unpack('<i', p.read(4))[0]
    it['cd']     = [(r7(p), r7(p)) for _ in range(cd)]
    it['wlevel'] = struct.unpack('<i', p.read(4))[0]
    it['picked'] = p.read(1)
    return it

def write_item(b, it, idx, width):
    w7(b, it['name'])
    b.write(struct.pack('<i', it['stack']))
    b.write(struct.pack('<f', it['dur']))
    b.write(struct.pack('<i', idx % width))
    b.write(struct.pack('<i', idx // width))
    b.write(it['equip'])
    b.write(struct.pack('<i', it['qual']))
    b.write(struct.pack('<i', it['var']))
    b.write(it['crafter'])
    w7(b, it['cname'])
    b.write(struct.pack('<i', len(it['cd'])))
    for k, v in it['cd']: w7(b, k); w7(b, v)
    b.write(struct.pack('<i', it['wlevel']))
    b.write(it['picked'])

def read_file(path):
    s = io.BytesIO(open(path, 'rb').read())
    magic, ver, count = struct.unpack('<I', s.read(4))[0], struct.unpack('<i', s.read(4))[0], struct.unpack('<i', s.read(4))[0]
    assert magic == 0x424C4331, f"{path}: not a bottomless store"
    out = {}
    for _ in range(count):
        sid = r7(s); ticks = struct.unpack('<q', s.read(8))[0]
        length = struct.unpack('<i', s.read(4))[0]
        p = io.BytesIO(s.read(length))
        inv_ver = struct.unpack('<i', p.read(4))[0]
        n = struct.unpack('<i', p.read(4))[0]
        out[sid] = (ticks, inv_ver, [read_item(p) for _ in range(n)])
    return out

WIDTH = 8
target = sys.argv[1]
sources = sys.argv[2:]

merged = {}
for src in sources:
    for sid, (ticks, inv_ver, items) in read_file(src).items():
        cur = merged.setdefault(sid, (ticks, inv_ver, []))
        seen = {(i['name'], i['stack'], i['qual'], i['var']) for i in cur[2]}
        for it in items:
            key = (it['name'], it['stack'], it['qual'], it['var'])
            if key not in seen:
                cur[2].append(it); seen.add(key)
        merged[sid] = (max(cur[0], ticks), inv_ver, cur[2])

out = io.BytesIO()
out.write(struct.pack('<I', 0x424C4331))
out.write(struct.pack('<i', 1))
out.write(struct.pack('<i', len(merged)))
for sid, (ticks, inv_ver, items) in merged.items():
    inv = io.BytesIO()
    inv.write(struct.pack('<i', inv_ver))
    inv.write(struct.pack('<i', len(items)))
    for i, it in enumerate(items): write_item(inv, it, i, WIDTH)
    blob = inv.getvalue()
    w7(out, sid); out.write(struct.pack('<q', ticks)); out.write(struct.pack('<i', len(blob))); out.write(blob)
    print(f"store {sid}: {len(items)} stacks")

open(target, 'wb').write(out.getvalue())
print(f"wrote {target} ({os.path.getsize(target)} bytes)")
