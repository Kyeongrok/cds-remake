# -*- coding: utf-8 -*-
"""
ESRGAN 계열 업스케일 모델(.pth)을 앱이 읽는 ONNX 로 바꾼다.

왜 바꾸나 — 앱은 ONNX Runtime 으로만 모델을 돌린다(CdsHelper.Support 의 ShipRemaster.cs).
OpenModelDB 에 올라 있는 것은 PyTorch .pth 라서 한 번 바꿔 고정 릴리즈 model-assets 에
올려 두고, 편집기의 「모델 받기」가 그것을 받는다.

지금 쓰는 모델은 Rebout Blend 4x 다.
    https://openmodeldb.info/models/4x-Rebout-Blend
    만든 이 LyonHrt · CC BY-NC-SA 4.0 · ESRGAN 64nf 23nb · 4배

폭과 높이는 정해 두지 않는다(dynamic axes) — 48x48 말고 다른 크기도 그대로 넣을 수 있다.
입력·출력은 RGB, 0~1, NCHW 다.

쓰는 법:
    python tools/convert_upscaler_onnx.py 4x-ReboutBlend.pth 4x-ReboutBlend.onnx

torch · spandrel · onnx · onnxruntime 이 있어야 한다.
"""
import argparse
import sys

import numpy as np
import onnxruntime as ort
import torch
from spandrel import ModelLoader


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("pth")
    ap.add_argument("onnx")
    ap.add_argument("--opset", type=int, default=17)
    args = ap.parse_args()

    # spandrel 은 피클을 제한해 읽는다 — 받아 온 .pth 를 torch.load 로 그냥 열지 않는다.
    loaded = ModelLoader(device="cpu").load_from_file(args.pth)
    print(f"{loaded.architecture.name}  x{loaded.scale}  in {loaded.input_channels}ch")
    model = loaded.model.eval()

    sample = torch.rand(1, loaded.input_channels, 48, 48)
    torch.onnx.export(
        model, sample, args.onnx,
        input_names=["input"], output_names=["output"],
        dynamic_axes={"input": {2: "h", 3: "w"}, "output": {2: "H", 3: "W"}},
        opset_version=args.opset, dynamo=False)

    # 바꾼 것이 같은 답을 내는지 견준다.
    with torch.no_grad():
        want = model(sample).numpy()
    got = ort.InferenceSession(args.onnx, providers=["CPUExecutionProvider"]).run(
        None, {"input": sample.numpy()})[0]
    worst = float(np.abs(want - got).max())
    print(f"출력 {got.shape}  가장 큰 차이 {worst:.6f}")
    if worst > 1e-3:
        sys.exit("ONNX 출력이 원본과 다릅니다")


if __name__ == "__main__":
    main()
