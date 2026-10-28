# Crops the fictional-data preview screenshots into figures for the guide (<= 900 px wide).
from PIL import Image
import os
SRC = '/home/claude/front/tools/ceo-preview/out/'
OUT = '/home/claude/kitceo/shots/'
os.makedirs(OUT, exist_ok=True)

def crop(name, src, box, maxw=900):
    im = Image.open(SRC + src).convert('RGB')
    c = im.crop(box)
    if c.width > maxw:
        h = round(c.height * maxw / c.width)
        c = c.resize((maxw, h), Image.LANCZOS)
    c.save(OUT + name, optimize=True)
    print(name, c.size, os.path.getsize(OUT + name))

crop('f01-top.png', 'ceo-1440-light.png', (0, 56, 1220, 830))
crop('f02-kpi.png', 'ceo-1440-light.png', (0, 640, 1220, 1160))
crop('f03-section.png', 'ceo-1440-light.png', (352, 5010, 1196, 6000))
crop('f04-alerts.png', 'ceo-1440-voice-drawer.png', (430, 50, 1195, 770))
crop('f05-drill.png', 'ceo-1440-drill.png', (0, 0, 940, 900))
crop('f06-analysis.png', 'ceo-1440-analysis.png', (780, 50, 1195, 890))
crop('f07-assistant.png', 'ceo-1440-assistant.png', (0, 56, 1220, 900))
crop('f08-listening.png', 'ceo-1440-voice-listening.png', (0, 600, 720, 900))
crop('f09-speaking.png', 'ceo-1440-voice-speaking.png', (0, 0, 940, 900))
crop('f10-filter.png', 'ceo-1440-voice-filter.png', (0, 0, 1340, 900))
crop('f11-pmo.png', 'ceo-1440-pmo.png', (355, 70, 1195, 900))
crop('f12-dark.png', 'ceo-1440-dark.png', (0, 56, 1220, 830))
crop('f13-mobile.png', 'ceo-390.png', (0, 0, 390, 1500))
crop('f14-print.png', 'ceo-print.png', (0, 0, 1100, 1010))
