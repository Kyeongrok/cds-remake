# -*- coding: utf-8 -*-
"""
타이틀 화면 바탕에서 반복 무늬 한 칸을 잘라 asset/title/title-tile.png 로 저장한다.

앱은 이 한 칸을 바둑판처럼 깔아 타이틀 바탕을 만든다
(CdsHelper.Game/UI/Views/ShipMapWindow.cs 의 TitleBackground).
화면 전체를 그대로 넣지 않는 것은 창 크기가 제각각이라서다 — 한 칸만 두면
어떤 크기에서도 무늬가 이어진다.

주기는 눈으로 재지 않고 여기서 찾는다. 가로·세로로 한 칸씩 밀어 보며 원본과 가장
덜 어긋나는 거리를 고르고, 그 크기로 잘랐을 때 좌우·위아래 이음매가 가장 얕은
자리를 다시 고른다. 지금 원본(tools/title-source.png)에서는 140x112 가 나왔고
이음매 오차가 0 이라 자국 없이 이어진다.

원본은 게임 타이틀 화면을 찍어 메뉴 상자를 뺀 것이다. 새 원본으로 다시 뜨려면
그 파일을 tools/title-source.png 에 두고 이 스크립트를 다시 돌려라.

쓰는 법:
    python tools/cut_title_tile.py
"""
import os

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "tools", "title-source.png")
OUT = os.path.join(ROOT, "asset", "title", "title-tile.png")

# 주기를 찾을 범위(픽셀). 무늬 한 칸이 이보다 크면 늘려라.
MIN_PERIOD, MAX_PERIOD = 8, 400


def best_period(a, axis):
    """한 칸씩 밀어 보며 원본과 가장 덜 어긋나는 거리를 고른다."""
    limit = min(a.shape[1 - axis] // 2, MAX_PERIOD)
    best = None
    for p in range(MIN_PERIOD, limit):
        d = (a[:, p:, :] - a[:, :-p, :]) if axis == 0 else (a[p:, :, :] - a[:-p, :, :])
        err = np.abs(d).mean()
        if best is None or err < best[0]:
            best = (err, p)
    return best


def best_origin(a, pw, ph):
    """그 크기로 잘랐을 때 좌우·위아래 이음매가 가장 얕은 자리."""
    h, w, _ = a.shape
    best = None
    for y0 in range(h - ph):
        for x0 in range(w - pw):
            seam_x = np.abs(a[y0:y0 + ph, x0, :] - a[y0:y0 + ph, x0 + pw, :]).mean()
            seam_y = np.abs(a[y0, x0:x0 + pw, :] - a[y0 + ph, x0:x0 + pw, :]).mean()
            s = seam_x + seam_y
            if best is None or s < best[0]:
                best = (s, x0, y0)
    return best


def main():
    img = Image.open(SRC).convert("RGB")
    a = np.asarray(img).astype(np.float64)
    print(f"원본 {img.size[0]}x{img.size[1]}")

    err_x, pw = best_period(a, 0)
    err_y, ph = best_period(a, 1)
    print(f"주기 {pw}x{ph} (어긋남 가로 {err_x:.4f} 세로 {err_y:.4f})")

    seam, x0, y0 = best_origin(a, pw, ph)
    print(f"자를 자리 ({x0}, {y0}) — 이음매 {seam:.4f}")

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    img.crop((x0, y0, x0 + pw, y0 + ph)).save(OUT)
    print(f"저장 {OUT}")


if __name__ == "__main__":
    main()
