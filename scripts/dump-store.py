#!/usr/bin/env python3
"""Dumps a .bottomless.dat sidecar file so contents can be checked outside the game."""
import struct, io, sys, glob

paths = sys.argv[1:] or glob.glob(
    "/mnt/c/Users/myrri/AppData/LocalLow/IronGate/Valheim/worlds_local/*.bottomless.dat*")

for path in paths:
    data = open(path, 'rb').read()
    print(f"\n=== {path}  ({len(data)} bytes) ===")
    s = io.BytesIO(data)
    rd = lambda f, n: struct.unpack(f, s.read(n))[0]
    def s7(buf):
        n = sh = 0
        while True:
            byte = buf.read(1)[0]
            n |= (byte & 0x7F) << sh
            if not (byte & 0x80): break
            sh += 7
        return buf.read(n).decode('utf-8')

    magic, ver, count = rd('<I',4), rd('<i',4), rd('<i',4)
    print(f"magic=0x{magic:08X} version={ver} stores={count}")
    for _ in range(count):
        sid, ticks, length = s7(s), rd('<q',8), rd('<i',4)
        p = io.BytesIO(s.read(length))
        pr = lambda f, n: struct.unpack(f, p.read(n))[0]
        # Valheim 1.0 (item format 108+) narrowed the stack count to a ushort and changed
        # the per-item encoding completely. Reading a 109 payload with the 106 rules gives
        # a wrong count and then nonsense items, which is worse than saying so.
        inv_ver = pr('<i',4)
        items = pr('<H',2) if inv_ver >= 108 else pr('<i',4)
        print(f"\n  store {sid}  ({length} bytes, inv v{inv_ver}, {items} stacks)")
        if inv_ver != 106:
            print(f"    [items not decoded: this tool only reads format 106, not {inv_ver}]")
            continue
        for _ in range(items):
            name, stack = s7(p), pr('<i',4)
            pr('<f',4)
            gx, gy = pr('<i',4), pr('<i',4)
            p.read(1); qual, var = pr('<i',4), pr('<i',4)
            p.read(8); s7(p)
            for _ in range(pr('<i',4)): s7(p); s7(p)
            pr('<i',4); p.read(1)
            print(f"    {name:<22} x{stack:<5} grid=({gx},{gy})")
