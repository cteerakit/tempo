"""Generates src/Tempo.App/Assets/Tempo.ico from tools/tempo-icon.svg.

Each frame is rendered straight from the vector source by headless Chrome or Edge
(transparent background) and stored as an embedded PNG. No Python packages needed.
Usage: python tools/make_icon.py src/Tempo.App/Assets/Tempo.ico
"""
import os, pathlib, shutil, struct, subprocess, sys, tempfile

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
SVG = pathlib.Path(__file__).with_name('tempo-icon.svg')
BROWSERS = [
    r'C:\Program Files\Google\Chrome\Application\chrome.exe',
    r'C:\Program Files (x86)\Google\Chrome\Application\chrome.exe',
    r'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe',
    r'C:\Program Files\Microsoft\Edge\Application\msedge.exe',
]

def find_browser():
    for p in BROWSERS:
        if os.path.exists(p):
            return p
    for name in ('chrome', 'msedge'):
        p = shutil.which(name)
        if p:
            return p
    sys.exit('Chrome or Edge is required to rasterize the SVG.')

def render(browser, tmp, size):
    html = tmp / f'icon{size}.html'
    png = tmp / f'icon{size}.png'
    html.write_text(
        '<!doctype html><meta charset="utf-8"><style>html,body{margin:0;background:transparent}'
        f'img{{display:block;width:{size}px;height:{size}px}}</style>'
        f'<img src="{SVG.resolve().as_uri()}">', encoding='utf-8')
    subprocess.run([
        browser, '--headless=new', '--disable-gpu', '--hide-scrollbars',
        '--default-background-color=00000000', f'--window-size={size},{size}',
        f'--user-data-dir={tmp / "profile"}', f'--screenshot={png}', html.as_uri(),
    ], check=True, capture_output=True)
    data = png.read_bytes()
    w, h = struct.unpack('>II', data[16:24])
    if (w, h) != (size, size):
        sys.exit(f'Expected a {size}px frame but the browser produced {w}x{h}.')
    return data

def main(out):
    browser = find_browser()
    with tempfile.TemporaryDirectory() as t:
        tmp = pathlib.Path(t)
        frames = [render(browser, tmp, s) for s in SIZES]
    data = struct.pack('<HHH', 0, 1, len(frames))
    offset = 6 + 16 * len(frames)
    for s, f in zip(SIZES, frames):
        dim = 0 if s >= 256 else s
        data += struct.pack('<BBBBHHII', dim, dim, 0, 0, 1, 32, len(f), offset)
        offset += len(f)
    data += b''.join(frames)
    pathlib.Path(out).write_bytes(data)

if __name__ == '__main__':
    main(sys.argv[1])
