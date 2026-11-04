"""Regenerates src/PlaylistMaker/app.ico (a rounded tile with playlist lines and a play mark).
Run: python tools/generate-icon.py
"""

from pathlib import Path

from PIL import Image, ImageDraw

SIZE = 256
BACKGROUND = (47, 111, 235, 255)
SHADOW = (26, 74, 168, 255)
MARK = (255, 255, 255, 255)
LINE = (206, 222, 250, 255)


def draw_icon() -> Image.Image:
    image = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((10, 10, SIZE - 10, SIZE - 10), radius=52, fill=SHADOW)
    draw.rounded_rectangle((10, 4, SIZE - 10, SIZE - 16), radius=52, fill=BACKGROUND)

    for index, width in enumerate((116, 116, 80)):
        top = 84 + index * 36
        draw.rounded_rectangle((56, top, 56 + width, top + 16), radius=8, fill=LINE)

    draw.polygon([(176, 104), (176, 180), (236, 142)], fill=MARK)
    return image


def main() -> None:
    target = Path(__file__).resolve().parent.parent / "src" / "PlaylistMaker" / "app.ico"
    image = draw_icon()
    sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
    image.save(target, format="ICO", sizes=sizes)
    preview = target.with_name("app-preview.png")
    image.resize((256, 256), Image.LANCZOS).save(preview, format="PNG")
    print("wrote", target, "and", preview.name)


if __name__ == "__main__":
    main()
