#!/usr/bin/env python3
"""Merges chest stores from several sidecar files into one, repacking grid positions.

Used to recover contents from a .old backup after a bad save.

Sources are treated as snapshots of the same chest at different times, so a stack
present in two of them is the same stack seen twice, not two stacks. Identical
entries are therefore counted, not collapsed: the result holds as many copies of
an entry as the source that held the most. Summing them would invent items;
keeping one would destroy them.

That distinction is the whole point of this script. Deduplicating on identity
alone once turned three Wood x50 stacks into one - a hundred wood destroyed by
the tool being used to recover a hundred and fifty-three.
"""
import struct, io, sys, os
from collections import Counter

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

def signature(it):
    """Everything that distinguishes one entry from another, except where it sat.

    Grid position is excluded because the merge repacks anyway. Everything else is
    included: two swords alike in name and stack size are still different swords if
    their durability, crafter or custom data differ, and collapsing them would
    silently destroy one item's condition.
    """
    return (it['name'], it['stack'], it['dur'], it['qual'], it['var'], it['equip'],
            it['crafter'], it['cname'], tuple(it['cd']), it['wlevel'], it['picked'])

target = sys.argv[1]
sources = sys.argv[2:]

merged = {}
for src in sources:
    for sid, (ticks, inv_ver, items) in read_file(src).items():
        cur_ticks, cur_ver, cur_items = merged.setdefault(sid, (ticks, inv_ver, []))
        held = Counter(signature(i) for i in cur_items)
        seen_here = Counter()
        for it in items:
            key = signature(it)
            seen_here[key] += 1
            # Take this copy only where this source holds more of the entry than we
            # already do. Per distinct entry the result is the largest count any one
            # source had - never the sum, which would conjure items out of a backup.
            if seen_here[key] > held[key]:
                cur_items.append(it)
        merged[sid] = (max(cur_ticks, ticks), inv_ver, cur_items)

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
