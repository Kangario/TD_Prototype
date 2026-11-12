# Генератор текстур для Arcane-стиля (папка Source~ Unity не импортирует).
#   T_ArcaneBrush.png  512x512, бесшовная, линейная:
#       R - мазки кисти (вытянутые, по полю направлений), G - мелкое зерно, B - штриховка (1 = бумага, 0 = линия),
#       A - крупный мягкий шум (пятна цвета, тени облаков)
#   T_CloudPuffs.png   512x512, атлас 2x2 облачков:
#       RG - нормаль облачка (xy * 0.5 + 0.5, x вправо, y вверх), B - плотность (0 снаружи, 1 в ядре), A - шум края
# Запуск: python gen_arcane_textures.py  (пишет PNG на уровень выше)
import os
import numpy as np
from PIL import Image

OUT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
N = 512
rng = np.random.default_rng(1337)


def periodic_noise(n, power, cutoff=None, seed=0):
    """Бесшовный шум через FFT: спектр 1/f^power, 0..1."""
    r = np.random.default_rng(seed)
    white = r.standard_normal((n, n))
    fx = np.fft.fftfreq(n)[:, None]
    fy = np.fft.fftfreq(n)[None, :]
    f = np.sqrt(fx * fx + fy * fy)
    f[0, 0] = 1.0
    filt = 1.0 / f ** power
    if cutoff is not None:
        filt *= np.exp(-(f / cutoff) ** 2)
    filt[0, 0] = 0.0
    out = np.real(np.fft.ifft2(np.fft.fft2(white) * filt))
    out -= out.min()
    out /= out.max()
    return out


def normalize(a, mean=0.5, std=0.18):
    a = (a - a.mean()) / (a.std() + 1e-6)
    return np.clip(a * std + mean, 0.0, 1.0)


# ---------------------------------------------------------------- R: мазки
def brush_strokes():
    canvas = np.full((N, N), 0.5)
    angle_field = periodic_noise(N, 2.2, seed=11) * np.pi * 1.6 + 0.3
    tone_field = periodic_noise(N, 1.6, seed=12)
    count = 3200
    for _ in range(count):
        cx, cy = rng.uniform(0, N, 2)
        ang = angle_field[int(cy) % N, int(cx) % N] + rng.normal(0, 0.18)
        length = rng.uniform(16, 46)
        width = rng.uniform(4.5, 10.0)
        tone = np.clip(tone_field[int(cy) % N, int(cx) % N] * 0.75 + rng.uniform(-0.25, 0.25) + 0.12, 0, 1)
        half = int(length * 0.5 + width + 3)
        xs = np.arange(int(cx) - half, int(cx) + half + 1)
        ys = np.arange(int(cy) - half, int(cy) + half + 1)
        X, Y = np.meshgrid(xs, ys)
        dx, dy = X - cx, Y - cy
        ca, sa = np.cos(ang), np.sin(ang)
        u = dx * ca + dy * sa            # вдоль мазка
        v = -dx * sa + dy * ca           # поперёк
        t = np.clip(u / (length * 0.5), -1, 1)
        w = width * 0.5 * (1.0 - 0.55 * t * t) * (1.0 + 0.12 * np.sin(u * 0.35 + rng.uniform(0, 6)))
        inside = (np.abs(u) <= length * 0.5) & (np.abs(v) <= w)
        edge_u = np.clip((length * 0.5 - np.abs(u)) / 3.0, 0, 1)
        edge_v = np.clip((w - np.abs(v)) / 1.5, 0, 1)
        alpha = edge_u * edge_v * inside
        # «щетина»: продольные полоски внутри мазка, к концу мазок сохнет
        bristle = 0.09 * np.sin(v * rng.uniform(1.6, 2.6) + rng.uniform(0, 6.28))
        dry = 1.0 - 0.5 * np.clip((u / (length * 0.5)) * rng.choice([-1, 1]), 0, 1) ** 2
        val = tone + bristle
        a = alpha * dry * rng.uniform(0.75, 1.0)
        iy, ix = Y % N, X % N
        canvas[iy, ix] = canvas[iy, ix] * (1 - a) + val * a
    return normalize(canvas, 0.5, 0.2)


# ---------------------------------------------------------------- G: зерно
def grain():
    g = periodic_noise(N, 0.35, cutoff=0.35, seed=21)
    coarse = periodic_noise(N, 1.2, seed=22)
    return normalize(g * 0.8 + coarse * 0.2, 0.5, 0.17)


# ---------------------------------------------------------------- B: штриховка
def hatching():
    y, x = np.mgrid[0:N, 0:N].astype(np.float64)
    period = 16.0                                     # 512 / 16 = 32 линии - бесшовно по диагонали
    warp = (periodic_noise(N, 2.0, seed=31) - 0.5) * 5.0
    phase = ((x + y + warp) / period) % 1.0
    d = np.abs(phase - 0.5) * period                 # расстояние до центра линии, px
    thick = 1.2 + 1.6 * periodic_noise(N, 1.8, seed=32)
    line = np.clip((thick - d) / 1.0 + 0.5, 0, 1)
    broken = np.clip((periodic_noise(N, 1.4, seed=33) - 0.32) * 4.0, 0, 1)
    ink = line * broken
    return 1.0 - ink


# ---------------------------------------------------------------- A: крупный шум
def big_noise():
    a = periodic_noise(N, 2.4, seed=41) * 0.75 + periodic_noise(N, 1.6, seed=42) * 0.25
    return normalize(a, 0.5, 0.2)


def save_brush():
    r = brush_strokes()
    g = grain()
    b = hatching()
    a = big_noise()
    img = np.stack([r, g, b, a], axis=-1)
    Image.fromarray((img * 255 + 0.5).astype(np.uint8), "RGBA").save(os.path.join(OUT, "T_ArcaneBrush.png"))


# ---------------------------------------------------------------- облачка
def gaussian_blur(a, sigma):
    n = a.shape[0]
    fx = np.fft.fftfreq(n)[:, None]
    fy = np.fft.fftfreq(n)[None, :]
    g = np.exp(-2 * (np.pi * sigma) ** 2 * (fx * fx + fy * fy))
    return np.real(np.fft.ifft2(np.fft.fft2(a) * g))


def cloud_puff(size, seed):
    r = np.random.default_rng(seed)
    # координаты в «uv» тайла: x вправо, y вверх, 0..1; массив - строками сверху вниз (как картинка)
    y, x = np.mgrid[0:size, 0:size].astype(np.float64)
    x = (x + 0.5) / size
    y = 1.0 - (y + 0.5) / size
    warp_x = (periodic_noise(size, 2.2, seed=seed + 1) - 0.5) * 0.07
    warp_y = (periodic_noise(size, 2.2, seed=seed + 2) - 0.5) * 0.07
    xw, yw = x + warp_x, y + warp_y

    base_y = 0.26
    spheres = []
    k = int(r.integers(4, 6))
    for i in range(k):                                   # нижний ряд - широкий и плоский
        cx = 0.24 + 0.52 * i / (k - 1) + r.uniform(-0.02, 0.02)
        rr = r.uniform(0.12, 0.15)
        spheres.append((cx, base_y + rr * 0.45, rr))
    for _ in range(int(r.integers(2, 4))):               # шапки сверху
        rr = r.uniform(0.15, 0.21)
        spheres.append((r.uniform(0.34, 0.66), base_y + r.uniform(0.16, 0.25), rr))

    h = np.zeros_like(x)
    for cx, cy, rr in spheres:
        d2 = (xw - cx) ** 2 + (yw - cy) ** 2
        h = np.maximum(h, np.sqrt(np.clip(rr * rr - d2, 0, None)))
    h *= np.clip((yw - (base_y - 0.02)) / 0.06, 0, 1) ** 0.5     # плоское дно
    h = gaussian_blur(h, 3.0)                                     # мягкие ложбинки между «лепестками»
    h = np.clip(h, 0, None)

    # нормаль из градиента высоты (в единицах uv)
    dhdy_img, dhdx = np.gradient(h, 1.0 / size)
    dhdy = -dhdy_img                                              # строки идут вниз, uv.y - вверх
    n = np.stack([-dhdx, -dhdy, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    dens = np.clip(h / (0.8 * h.max()), 0, 1) ** 0.6
    detail = normalize(periodic_noise(size, 1.3, seed=seed + 3), 0.5, 0.2)
    return n, dens, detail


def save_clouds():
    tile = N // 2
    atlas = np.zeros((N, N, 4))
    # строим в uv (y вверх), потом переворачиваем
    for idx in range(4):
        n, dens, detail = cloud_puff(tile, 100 + idx * 10)
        col, row = idx % 2, idx // 2
        rgba = np.stack([n[..., 0] * 0.5 + 0.5, n[..., 1] * 0.5 + 0.5, dens, detail], -1)
        # cloud_puff уже вернул картинку строками сверху вниз (y = 1 - row), кладём тайл в нужную четверть
        y0 = (1 - row) * tile
        atlas[y0:y0 + tile, col * tile:(col + 1) * tile] = rgba
    Image.fromarray((atlas * 255 + 0.5).astype(np.uint8), "RGBA").save(os.path.join(OUT, "T_CloudPuffs.png"))


if __name__ == "__main__":
    save_brush()
    save_clouds()
    print("ok:", OUT)
