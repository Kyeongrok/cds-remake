# -*- coding: utf-8 -*-
"""
실행 중인 CDS_95 에서 함대 그림을 떠서 asset/ship/*.png 로 저장한다.

왜 게임을 켜야 하나 — 그림이 EXE 파일에는 없다. 자리가 .data 의 초기화되지 않은
뒷부분(rawsize 0x51C00 밖)이라 실행 중에만 채워진다. 그래서 한 번 떠서 파일로 남기고,
앱은 그 파일을 읽는다(CdsHelper.Game/Local/Helpers/ShipSprites.cs).

★ 반드시 해상(항해) 화면에서 떠라. 아틀라스는 게임이 그때그때 채우는 버퍼라서, 항구
  안이나 타이틀 화면에서 뜨면 엉뚱한 그림이 나온다(실제로 겪었다 — 같은 자리에서
  안 비치는 점이 404개에서 138개로 줄어든 다른 그림이 나왔다).
  뜬 뒤에는 뜬 그림을 열어 배 모양인지 눈으로 확인해라.

같은 클래스라도 함대에 태운 배가 다르면 아틀라스 내용이 달라진다. 다른 배를 따로
남기려면 --out 으로 폴더를 나눠라:
    python tools/extract_ship_sprites.py --out asset/ship-galleon

자리와 고르는 규칙은 cds95-mod plugins-src/WorldMapKR/src/sprite.c 에서 가져왔고,
그쪽은 게임 렌더러 0x48A1E0(0x48A82E~0x48A8A4)에서 읽어낸 것이다.
게임 함수는 부르지 않고 값만 짚는다.

    프레임      한 장 48x48 팔레트 색인, 색인 0 이 비침(2304바이트)
    배 아틀라스  0x5D68C8 — 4벌(함선 클래스) x 8방향
    말 아틀라스  0x6092D0 — 육상·정박
    방향        게임은 반시계로 돈다: 0 북, 4 서, 8 남, 12 동. 16방향을 8장으로 접는다
    색          OceanPalette.cs 의 256색을 그대로 쓴다

쓰는 법 (게임을 켜 둔 채로):
    python tools/extract_ship_sprites.py              # 클래스 0 을 asset/ship 에
    python tools/extract_ship_sprites.py --class 2    # 다른 함선 클래스
    python tools/extract_ship_sprites.py --land       # 말(정박) 그림
    python tools/extract_ship_sprites.py --out asset/horse

PIL(pillow) 이 있어야 한다.
"""
import argparse
import ctypes
import ctypes.wintypes as wt
import os
import re
import sys

ATLAS_SEA = 0x5D68C8
ATLAS_LAND = 0x6092D0
LAND_BASE = 0x569550
SPR_W = 48
SPR_SZ = SPR_W * SPR_W
DIRECTIONS = 8

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PALETTE_CS = os.path.join(ROOT, "CdsHelper.Support", "Local", "Helpers", "OceanPalette.cs")

PROCESS_VM_READ = 0x0010
PROCESS_QUERY_INFORMATION = 0x0400

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
k32.OpenProcess.restype = wt.HANDLE
k32.ReadProcessMemory.argtypes = [wt.HANDLE, wt.LPCVOID, wt.LPVOID,
                                  ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]


def find_pid(name="cds_95.exe"):
    """이름으로 프로세스 번호를 찾는다. 없으면 None."""
    import subprocess
    out = subprocess.run(["tasklist", "/FI", f"IMAGENAME eq {name}", "/FO", "CSV", "/NH"],
                         capture_output=True, text=True).stdout
    m = re.search(r'"[^"]+","(\d+)"', out)
    return int(m.group(1)) if m else None


def read(handle, address, size):
    buf = (ctypes.c_ubyte * size)()
    got = ctypes.c_size_t()
    ok = k32.ReadProcessMemory(handle, ctypes.c_void_p(address), buf, size, ctypes.byref(got))
    if not ok or got.value != size:
        raise OSError(f"0x{address:X} 에서 {size}바이트를 못 읽었다 (Win32 {ctypes.get_last_error()})")
    return bytes(buf)


def load_palette():
    """앱이 쓰는 것과 같은 256색을 OceanPalette.cs 에서 읽는다."""
    src = open(PALETTE_CS, encoding="utf-8-sig").read()
    body = src.split("Rgb =", 1)[1]
    body = body[body.index("[") + 1:body.index("];")]
    vals = [int(x) for x in re.findall(r"\d+", body)]
    if len(vals) != 768:
        raise SystemExit(f"팔레트가 768개가 아니다: {len(vals)}")
    return vals


def main(argv):
    ap = argparse.ArgumentParser(description="게임에서 함대 그림을 떠서 PNG 로 저장한다")
    ap.add_argument("--class", dest="cls", type=int, default=0, choices=range(4),
                    help="함선 그림 클래스 0~3 (배 아틀라스는 4벌이다)")
    ap.add_argument("--land", action="store_true", help="배 대신 말(육상·정박) 그림")
    ap.add_argument("--out", default=os.path.join("asset", "ship"), help="저장할 폴더")
    ap.add_argument("--prefix", default=None, help="파일 이름 앞머리. 기본은 배 ship, 말 horse")
    ap.add_argument("--force", action="store_true",
                    help="(남겨 둔 것) 예전에 덮어쓰기를 막던 스위치. 지금은 늘 덮어쓴다")
    a = ap.parse_args(argv[1:])
    prefix = a.prefix or ("horse" if a.land else "ship")

    pid = find_pid()
    if pid is None:
        raise SystemExit("cds_95.exe 를 못 찾았다 — 게임을 켠 채로 실행해라")
    handle = k32.OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, False, pid)
    if not handle:
        raise SystemExit(f"프로세스 {pid} 를 못 열었다 (Win32 {ctypes.get_last_error()})")
    print(f"cds_95.exe pid {pid} 에 붙었다")

    palette = load_palette()
    outdir = a.out if os.path.isabs(a.out) else os.path.join(ROOT, a.out)
    os.makedirs(outdir, exist_ok=True)

    # 이미 있으면 알려만 주고 덮어쓴다. 잘못 떴으면 다시 뜨면 그만이라 막지 않는다.
    existing = [d for d in range(DIRECTIONS)
                if os.path.exists(os.path.join(outdir, f"{prefix}_{d}.png"))]
    if existing:
        print(f"{os.path.relpath(outdir, ROOT)} 의 {len(existing)}장을 덮어쓴다")

    if a.land:
        base = int.from_bytes(read(handle, LAND_BASE, 4), "little")
        print(f"말 그림 밑번호 {base}")

    from PIL import Image
    for d in range(DIRECTIONS):
        heading = d * 2                       # 16방향을 8장으로 접는다
        if a.land:
            dd = (heading + 1) & 0xF
            frame = (dd >> 2) * 8 + base
            atlas = ATLAS_LAND
        else:
            frame = a.cls * 8 + (heading >> 1)
            atlas = ATLAS_SEA

        raw = read(handle, atlas + frame * SPR_SZ, SPR_SZ)
        im = Image.new("RGBA", (SPR_W, SPR_W))
        px = im.load()
        opaque = 0
        for y in range(SPR_W):
            for x in range(SPR_W):
                i = raw[y * SPR_W + x]
                if i == 0:
                    px[x, y] = (0, 0, 0, 0)
                else:
                    px[x, y] = (palette[i * 3], palette[i * 3 + 1], palette[i * 3 + 2], 255)
                    opaque += 1
        path = os.path.join(outdir, f"{prefix}_{d}.png")
        im.save(path)
        print(f"  방향 {heading:2} (프레임 {frame:2}) -> {os.path.relpath(path, ROOT)}  안 비치는 점 {opaque}")

    print(f"\n{DIRECTIONS}장을 {os.path.relpath(outdir, ROOT)} 에 저장했다")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
