#!/usr/bin/env python3
# -*- coding: utf-8 -*-
import numpy as np

# 기준점 데이터 (위도, 경도, 픽셀X, 픽셀Y)
reference_points = [
    (41, -8, 3540, 849),    # 오포르트
    (43, -3, 3668, 800),    # 빌바오
    (38, -9, 3527, 904),    # 리스본
    (38, 23, 4237, 924),    # 아테네
    (37, 126, 6515, 922),   # 한양
]

# 최소제곱법으로 변환 계수 계산
# 픽셀X = a0 + a1*경도 + a2*위도
# 픽셀Y = b0 + b1*경도 + b2*위도

# 행렬 구성
A = []
pixel_x = []
pixel_y = []

for lat, lon, px, py in reference_points:
    A.append([1, lon, lat])
    pixel_x.append(px)
    pixel_y.append(py)

A = np.array(A)
pixel_x = np.array(pixel_x)
pixel_y = np.array(pixel_y)

# 최소제곱법: (A^T A)^-1 A^T b
coeffs_x = np.linalg.lstsq(A, pixel_x, rcond=None)[0]
coeffs_y = np.linalg.lstsq(A, pixel_y, rcond=None)[0]

print("=== 변환 계수 (최소제곱법) ===")
print(f"픽셀X = {coeffs_x[0]:.6f} + {coeffs_x[1]:.6f}*경도 + {coeffs_x[2]:.6f}*위도")
print(f"픽셀Y = {coeffs_y[0]:.6f} + {coeffs_y[1]:.6f}*경도 + {coeffs_y[2]:.6f}*위도")

# 검증
print("\n=== 기준점 검증 ===")
names = ["오포르트", "빌바오", "리스본", "아테네", "한양"]
total_error_x = 0
total_error_y = 0
for i, (lat, lon, actual_px, actual_py) in enumerate(reference_points):
    calc_px = coeffs_x[0] + coeffs_x[1] * lon + coeffs_x[2] * lat
    calc_py = coeffs_y[0] + coeffs_y[1] * lon + coeffs_y[2] * lat
    error_x = calc_px - actual_px
    error_y = calc_py - actual_py
    total_error_x += abs(error_x)
    total_error_y += abs(error_y)
    print(f"{names[i]:10s} 실제({actual_px:4d}, {actual_py:4d}) 계산({calc_px:7.1f}, {calc_py:6.1f}) 오차({error_x:5.1f}, {error_y:5.1f})")

print(f"\n평균 절대 오차: X={total_error_x/len(reference_points):.2f}, Y={total_error_y/len(reference_points):.2f}")

print(f"\n\n=== Python 스크립트용 계수 ===")
print(f"COEFF_X_CONST = {coeffs_x[0]}")
print(f"COEFF_X_LON = {coeffs_x[1]}")
print(f"COEFF_X_LAT = {coeffs_x[2]}")
print(f"COEFF_Y_CONST = {coeffs_y[0]}")
print(f"COEFF_Y_LON = {coeffs_y[1]}")
print(f"COEFF_Y_LAT = {coeffs_y[2]}")
