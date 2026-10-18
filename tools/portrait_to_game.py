"""사람이 그린 초상화를 원본 얼굴 화풍(80x96 · 공용 색표 64색 · 디더링)으로 바꾼다.

원본 초상화는 80x96 에 공용 색표의 10~73번(64색)만 쓰고, 두 색을 바둑판으로 섞어 중간색을 낸다(GamePalette).
게임은 80x96 그림을 점을 살려(부드럽게 섞지 않고) 키워 거므로, 그림을 같은 꼴로 만들어 asset/face/male-90NN.png 로 넣으면
원본 후원자들과 같은 결로 보인다 — 큰 그림을 그대로 넣으면 매끈한 유화처럼 보여 따로 놀았다.

쓰는 법:  python tools/portrait_to_game.py <그림> <내보낼 png> [가운데x 가운데y 폭]
  가운데 · 폭은 원본 그림 좌표다. 얼굴(이마~턱)이 칸을 거의 채우게 자른다(폭:높이 = 5:6).
"""
import os, sys
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PALETTE = os.path.join(ROOT, 'tools', 'portrait_palette.txt')   # GamePalette 10~73 (R,G,B)

src, out = sys.argv[1], sys.argv[2]
im = Image.open(src).convert('RGB')
if len(sys.argv) >= 6:
    cx, cy, w = map(int, sys.argv[3:6])
else:
    cx, cy, w = im.width // 2, im.height // 2, min(im.width, im.height * 5 // 6)
h = w * 6 // 5
face = im.crop((cx - w // 2, cy - h // 2, cx + w // 2, cy + h // 2)).resize((80, 96), Image.LANCZOS)

pal = [tuple(map(int, l.split(','))) for l in open(PALETTE) if l.strip()]
flat = [c for p in pal for c in p] + list(pal[0]) * (256 - len(pal))
palimg = Image.new('P', (1, 1))
palimg.putpalette(flat)
face.quantize(palette=palimg, dither=Image.Dither.FLOYDSTEINBERG).convert('RGB').save(out)
print(out)
