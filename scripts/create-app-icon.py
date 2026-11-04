"""Convert the existing DesignSystem.AppIcon vector to a multi-size Windows ICO."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1]
target = root / 'src/GamePanel.Desktop/Assets/GamePanel.ico'
target.parent.mkdir(parents=True, exist_ok=True)
scale = 16
image = Image.new('RGBA', (64 * scale, 64 * scale), (0, 0, 0, 0))
draw = ImageDraw.Draw(image)
draw.rounded_rectangle((0, 0, 64 * scale - 1, 64 * scale - 1), radius=12 * scale, fill='#0A0F16')
for points, color in [([(12, 15), (23, 15), (40, 32), (23, 49), (12, 49), (29, 32)], '#BCFF69'),
                      ([(31, 15), (42, 15), (59, 32), (42, 49), (31, 49), (48, 32)], '#6BA9FF')]:
    draw.polygon([(x * scale, y * scale) for x, y in points], fill=color)
image.resize((256, 256), Image.Resampling.LANCZOS).save(target, format='ICO', sizes=[(n, n) for n in (16, 24, 32, 48, 64, 128, 256)])
print(target)
