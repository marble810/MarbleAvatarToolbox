"""固定 Body_Base 截图回归（需要 Pillow，仅用于开发验证，不是 Unity 依赖）。

用 UnityMCP 捕获 Scene viewport；投影文件格式为：首行 width,height，
后续每行 camera.WorldToScreenPoint(loopVertex) 的 x,y,z。
用法：python NeckMaskEdgeScreenshotRegression.py screenshot.png projected.txt
测试使用本次模型的第 5..9 段，避开其他黄色对象。
截图 GrabPixels / DPI 原点取整存在偏差，因此该图像检查允许 3.5 px；
精确线中心、角度和宽度由配套 GPU 回归覆盖。
"""
import math
import sys
from PIL import Image

image = Image.open(sys.argv[1]).convert("RGB")
with open(sys.argv[2], encoding="utf-8") as source:
    width, height = map(int, next(source).split(","))
    points = [list(map(float, line.split(","))) for line in source if line.strip()]
assert image.size == (width, height), "截图和相机分辨率不一致"
failures = []
for index in range(5, 10):
    a, b = points[index:index + 2]
    x = (a[0] + b[0]) / 2
    y = height - (a[1] + b[1]) / 2
    dx, dy = b[0] - a[0], a[1] - b[1]
    length = math.hypot(dx, dy)
    nx, ny = -dy / length, dx / length
    samples = []
    for distance in range(-22, 23):
        r, g, blue = image.getpixel((round(x + distance * nx), round(y + distance * ny)))
        yellow = max(0, g - blue - 15) if r > g else 0
        if yellow:
            samples.append((distance, yellow))
    center = sum(d * weight for d, weight in samples) / sum(weight for _, weight in samples) if samples else None
    thickness = len(samples)
    print(f"segment={index}, center_offset={center}, threshold_width={thickness}")
    if center is None or abs(center) > 3.5 or thickness < 3 or thickness > 5:
        failures.append(index)
if failures:
    raise SystemExit(f"FAIL: 错位或线宽异常的边段 {failures}")
print("PASS: 固定模型截图的边线位置和线宽")
