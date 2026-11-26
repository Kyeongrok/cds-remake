# -*- coding: utf-8 -*-
"""원본 초상화(MALE.CDS · FEMALE.CDS, 80x96)를 Rebout Blend 4x 로 네 배(320x384) 키워 내보낸다 — 모드 「초상화 업스케일링」.

도시 그림 업스케일링(city-hd)과 같은 길이다. 이 PC 에서 미리 키워 고정 릴리즈 portrait-hd 에
「그림 점 해시 이름.png」와 그 목록 index.txt 를 올리고, 앱은 모드가 켜지면 받아 쓴다(PortraitUpscale.cs).

이름은 앱의 CityUpscale.KeyOf 와 같아야 한다 — 80x96 BGRA(0xAARRGGBB, 리틀엔디안) 바이트의 SHA256 앞 20자(대문자).
그래서 그 PC 의 얼굴이 원본과 같을 때만 맞는다. 바꿔 넣은 얼굴에는 엉뚱한 그림이 붙지 않는다.

쓰는 법:  python tools/upscale_portraits.py <내보낼 폴더> [모델.onnx]
  모델은 기본으로 %LOCALAPPDATA%\\CdsHelper\\model\\4x-ReboutBlend.onnx (앱이 받아 두는 자리).
올리기:   gh release create portrait-hd -R Kyeongrok/cds-remake --title portrait-hd --notes "..." <폴더>/*
"""
import hashlib
import os
import struct
import sys

import numpy as np
import onnxruntime as ort
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, 'tools'))
from ls12 import Ls12  # noqa: E402

W, H = 80, 96
ASSETS = os.path.join(ROOT, 'CdsHelper.Support', 'Local', 'Assets')
PALETTE = os.path.join(ROOT, 'tools', 'portrait_palette.txt')   # GamePalette 10~73


def palette():
    # GamePalette: 0~9 는 회색 186, 10~73 은 표, 그 위는 (12,7,6).
    low = [tuple(map(int, l.split(','))) for l in open(PALETTE) if l.strip()]
    return [(186, 186, 186)] * 10 + low + [(12, 7, 6)] * (256 - 10 - len(low))


def key_of(rgb):
    data = bytearray()
    for r, g, b in rgb:
        data += bytes((b, g, r, 0xFF))
    return '%dx%d-%s' % (W, H, hashlib.sha256(bytes(data)).hexdigest().upper()[:20])


def main():
    out = sys.argv[1]
    model = sys.argv[2] if len(sys.argv) > 2 else os.path.join(
        os.environ['LOCALAPPDATA'], 'CdsHelper', 'model', '4x-ReboutBlend.onnx')
    os.makedirs(out, exist_ok=True)
    pal = palette()
    opts = ort.SessionOptions()
    session = ort.InferenceSession(model, opts, providers=['CPUExecutionProvider'])
    name = session.get_inputs()[0].name

    keys = []
    for file in ('MALE.CDS', 'FEMALE.CDS'):
        archive = Ls12.open(os.path.join(ASSETS, file))
        for i in range(len(archive)):
            idx = archive.decode(i)
            if idx is None or len(idx) < W * H:
                continue
            rgb = [pal[c] for c in idx[:W * H]]
            key = key_of(rgb)
            if key in keys:
                continue
            keys.append(key)
            path = os.path.join(out, key + '.png')
            if not os.path.exists(path):
                x = np.asarray(rgb, dtype=np.float32).reshape(H, W, 3).transpose(2, 0, 1)[None] / 255.0
                y = session.run(None, {name: x})[0][0]
                img = (np.clip(y, 0, 1) * 255 + 0.5).astype(np.uint8).transpose(1, 2, 0)
                Image.fromarray(img, 'RGB').save(path + '.tmp', 'PNG', optimize=True)
                os.replace(path + '.tmp', path)
            print('%s %d %s' % (file, i, key), flush=True)

    with open(os.path.join(out, 'index.txt'), 'w', newline='\n') as f:
        f.write('\n'.join(keys) + '\n')
    print('%d장' % len(keys))


if __name__ == '__main__':
    main()
