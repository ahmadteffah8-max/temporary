"""zat - قارئ صور بسيط.

يشتغل بس إذا فتحت صورة (فتح بواسطة ... zat). فيه: تنقل بين الصور، زوم بعجلة الماوس،
قلب الصورة، قلم بثلاث ألوان (أحمر / أخضر / أصفر) مع حفظ، ووضع داكن/عادي.
القلم ما يشتغل على الصور المتحركة.
"""
import math
import os
import re
import sys
import tkinter as tk
from tkinter import messagebox

from PIL import Image, ImageDraw, ImageOps, ImageSequence, ImageTk

try:  # دعم HEIC / HEIF لو المكتبة موجودة
    import pillow_heif

    pillow_heif.register_heif_opener()
    HEIF_EXTS = {".heic", ".heif"}
except Exception:  # noqa: BLE001
    HEIF_EXTS = set()

try:  # AVIF
    import pillow_avif  # noqa: F401
except Exception:  # noqa: BLE001
    pass

IMAGE_EXTS = {e.lower() for e in Image.registered_extensions()} | HEIF_EXTS
IMAGE_EXTS -= {".pdf", ".eps", ".ps", ".xpm"}  # مو صور عادية للعرض

PEN_COLORS = {"red": "#e53935", "green": "#43a047", "yellow": "#fdd835"}
PEN_SCREEN_WIDTH = 3

THEMES = {
    "dark": dict(bg="#1b1b1d", canvas="#121214", fg="#e6e6e6", btn="#2a2a2d",
                 border="#55555a", hover="#3a3a3e", icon="☀"),   # ☀ للتحويل للعادي
    "light": dict(bg="#ececee", canvas="#ffffff", fg="#1c1c1e", btn="#f8f8f9",
                  border="#a9a9ae", hover="#dcdce0", icon="☾"),  # ☾ للتحويل للداكن
}


def natural_key(name):
    return [int(t) if t.isdigit() else t.lower() for t in re.split(r"(\d+)", name)]


def is_image_file(path):
    return os.path.isfile(path) and os.path.splitext(path)[1].lower() in IMAGE_EXTS


def list_folder_images(path):
    folder = os.path.dirname(os.path.abspath(path))
    try:
        names = [n for n in os.listdir(folder) if is_image_file(os.path.join(folder, n))]
    except OSError:
        names = [os.path.basename(path)]
    names.sort(key=natural_key)
    files = [os.path.join(folder, n) for n in names]
    target = os.path.abspath(path)
    if target not in files:
        files.append(target)
    return files, files.index(target)


def load_image(path):
    """يرجع (frames, durations). صورة عادية = فريم واحد وdurations فاضي."""
    img = Image.open(path)
    animated = getattr(img, "is_animated", False) and getattr(img, "n_frames", 1) > 1
    if animated:
        frames, durs = [], []
        for fr in ImageSequence.Iterator(img):
            durs.append(max(20, int(fr.info.get("duration", 100) or 100)))
            frames.append(fr.convert("RGBA"))
        return frames, durs
    img = ImageOps.exif_transpose(img)
    img.load()
    return [img.convert("RGBA")], []


class Viewer:
    def __init__(self, root, path):
        self.root = root
        self.theme_name = "dark"
        self.files, self.idx = list_folder_images(path)

        self.frames = []
        self.durs = []
        self.frame_i = 0
        self.anim_job = None
        self.base = None
        self.scale = 1.0
        self.ox = self.oy = 0.0
        self.fit_mode = True
        self.tkimg = None

        self.pen_on = False
        self.pen_color = PEN_COLORS["red"]
        self.strokes = []  # [(color, width_img, [x, y, x, y, ...])]
        self.cur_stroke = None
        self.dirty = False
        self.pan_last = None
        self.status_job = None

        root.minsize(380, 260)
        self._build_ui()
        self.apply_theme()
        root.protocol("WM_DELETE_WINDOW", self.on_close)
        root.bind("<Left>", lambda e: self.step(-1))
        root.bind("<Right>", lambda e: self.step(1))
        root.bind("<Control-z>", lambda e: self.undo())
        root.bind("<Escape>", lambda e: self.set_pen(False))
        root.update_idletasks()
        self.load_current(first=True)

    # ---------------------------------------------------------------- UI
    def _btn(self, parent, text, cmd, width=3):
        return tk.Button(parent, text=text, command=cmd, width=width,
                         font=("Segoe UI Symbol", 9), bd=0, relief="flat",
                         highlightthickness=1, padx=1, pady=0, cursor="hand2",
                         takefocus=0)

    def _build_ui(self):
        r = self.root
        self.top = tk.Frame(r)
        self.top.pack(side="top", fill="x", padx=4, pady=(4, 2))
        self.bottom = tk.Frame(r)
        self.bottom.pack(side="bottom", fill="x", padx=4, pady=(2, 5))
        self.canvas = tk.Canvas(r, highlightthickness=0, bd=0)
        self.canvas.pack(fill="both", expand=True)

        # فوق يسار: وضع داكن/عادي
        self.theme_btn = self._btn(self.top, "", self.toggle_theme)
        self.theme_btn.pack(side="left")
        self.status = tk.Label(self.top, text="", font=("Segoe UI", 8), anchor="center")
        self.status.pack(side="left", fill="x", expand=True, padx=6)

        # فوق يمين: القلم، وجنبه الحفظ، وجنبه الألوان
        self.pen_btn = self._btn(self.top, "✎", self.toggle_pen)
        self.pen_btn.pack(side="right")
        self.save_btn = self._btn(self.top, "\U0001F4BE", self.save)
        self.colors = tk.Frame(self.top)
        self.swatches = {}
        for name, hexv in PEN_COLORS.items():
            b = tk.Button(self.colors, text="", width=2, bd=0, relief="flat",
                          bg=hexv, activebackground=hexv, highlightthickness=1,
                          cursor="hand2", takefocus=0, font=("Segoe UI", 7),
                          command=lambda h=hexv: self.set_color(h))
            b.pack(side="left", padx=1)
            self.swatches[hexv] = b

        # تحت بالنص: السابق، التالي، زوم، قلب
        self.nav = tk.Frame(self.bottom)
        self.nav.pack(side="top")
        self.prev_btn = self._btn(self.nav, "◀", lambda: self.step(-1))
        self.next_btn = self._btn(self.nav, "▶", lambda: self.step(1))
        self.zoom_btn = self._btn(self.nav, "\U0001F50D", self.toggle_zoom)
        self.flip_btn = self._btn(self.nav, "⇋", self.flip)
        for b in (self.prev_btn, self.next_btn, self.zoom_btn, self.flip_btn):
            b.pack(side="left", padx=2)

        c = self.canvas
        c.bind("<Configure>", self.on_resize)
        c.bind("<MouseWheel>", self.on_wheel)
        c.bind("<Button-4>", lambda e: self.zoom_at(e.x, e.y, 1.15))
        c.bind("<Button-5>", lambda e: self.zoom_at(e.x, e.y, 1 / 1.15))
        c.bind("<ButtonPress-1>", self.on_press)
        c.bind("<B1-Motion>", self.on_drag)
        c.bind("<ButtonRelease-1>", self.on_release)
        for n in (2, 3):
            c.bind(f"<ButtonPress-{n}>", self.on_pan_start)
            c.bind(f"<B{n}-Motion>", self.on_pan_move)

    def apply_theme(self):
        t = THEMES[self.theme_name]
        self.root.configure(bg=t["bg"])
        for f in (self.top, self.bottom, self.nav, self.colors):
            f.configure(bg=t["bg"])
        self.status.configure(bg=t["bg"], fg=t["fg"])
        self.canvas.configure(bg=t["canvas"])
        for b in (self.theme_btn, self.pen_btn, self.save_btn, self.prev_btn,
                  self.next_btn, self.zoom_btn, self.flip_btn):
            b.configure(bg=t["btn"], fg=t["fg"], activebackground=t["hover"],
                        activeforeground=t["fg"], highlightbackground=t["border"],
                        highlightcolor=t["border"], disabledforeground=t["border"])
        self.theme_btn.configure(text=t["icon"])
        for hexv, b in self.swatches.items():
            b.configure(highlightbackground=t["border"], highlightcolor=t["border"])
        self.refresh_pen_ui()
        self.render()

    def toggle_theme(self):
        self.theme_name = "light" if self.theme_name == "dark" else "dark"
        self.apply_theme()

    def say(self, text, ms=2500):
        self.status.configure(text=text)
        if self.status_job:
            self.root.after_cancel(self.status_job)
        self.status_job = self.root.after(ms, lambda: self.status.configure(text=""))

    # ------------------------------------------------------------ loading
    def stop_anim(self):
        if self.anim_job:
            self.root.after_cancel(self.anim_job)
            self.anim_job = None

    @property
    def animated(self):
        return len(self.frames) > 1

    def load_current(self, first=False):
        self.stop_anim()
        path = self.files[self.idx]
        try:
            self.frames, self.durs = load_image(path)
        except Exception as exc:  # noqa: BLE001
            if first:
                messagebox.showerror("zat", f"ما قدرت أفتح الصورة:\n{exc}")
                self.root.destroy()
                return
            self.say("ما قدرت أفتح هالصورة")
            self.frames, self.durs = [Image.new("RGBA", (400, 300), (60, 60, 60, 255))], []
        self.frame_i = 0
        self.base = self.frames[0]
        self.strokes, self.cur_stroke, self.dirty = [], None, False
        if self.animated:
            self.set_pen(False)
        self.refresh_pen_ui()
        name = os.path.basename(path)
        self.root.title(f"zat - {name}  ({self.idx + 1}/{len(self.files)})")
        if first:
            self.size_window()
        self.fit()
        if self.animated:
            self.anim_job = self.root.after(self.durs[0], self.next_frame)

    def size_window(self):
        sw, sh = self.root.winfo_screenwidth(), self.root.winfo_screenheight()
        w, h = self.base.size
        k = min((sw * 0.8) / max(w, 1), (sh * 0.8) / max(h, 1), 1.0)
        ww = max(520, int(w * k) + 10)
        wh = max(380, int(h * k) + 70)
        ww, wh = min(ww, int(sw * 0.9)), min(wh, int(sh * 0.9))
        self.root.geometry(f"{ww}x{wh}+{(sw - ww) // 2}+{(sh - wh) // 3}")
        self.root.update_idletasks()

    def next_frame(self):
        if not self.animated:
            return
        self.frame_i = (self.frame_i + 1) % len(self.frames)
        self.base = self.frames[self.frame_i]
        self.render()
        self.anim_job = self.root.after(self.durs[self.frame_i], self.next_frame)

    def step(self, d):
        if len(self.files) < 2 or not self.confirm_discard():
            return
        self.idx = (self.idx + d) % len(self.files)
        self.load_current()

    # --------------------------------------------------------- view / zoom
    def cwh(self):
        return max(self.canvas.winfo_width(), 1), max(self.canvas.winfo_height(), 1)

    def fit_scale(self):
        cw, ch = self.cwh()
        w, h = self.base.size
        return min(cw / w, ch / h, 1.0)

    def fit(self):
        cw, ch = self.cwh()
        w, h = self.base.size
        self.scale = self.fit_scale()
        self.ox = (cw - w * self.scale) / 2
        self.oy = (ch - h * self.scale) / 2
        self.fit_mode = True
        self.render()

    def on_resize(self, _e):
        if self.base is None:
            return
        if self.fit_mode:
            self.fit()
        else:
            self.render()

    def zoom_at(self, mx, my, factor):
        ns = min(max(self.scale * factor, 0.02), 60.0)
        if ns == self.scale:
            return
        k = ns / self.scale
        self.ox = mx - (mx - self.ox) * k
        self.oy = my - (my - self.oy) * k
        self.scale = ns
        self.fit_mode = False
        self.render()

    def on_wheel(self, e):
        self.zoom_at(e.x, e.y, 1.1 ** (e.delta / 120.0))

    def toggle_zoom(self):
        fs = self.fit_scale()
        cw, ch = self.cwh()
        if abs(self.scale - fs) < 0.01 or self.fit_mode:
            target = 1.0 if fs < 0.99 else 2.0
        else:
            self.fit()
            return
        self.zoom_at(cw / 2, ch / 2, target / self.scale)

    # ------------------------------------------------------------- render
    def render(self):
        c = self.canvas
        c.delete("img")
        c.delete("stroke")
        if self.base is None:
            return
        cw, ch = self.cwh()
        w, h = self.base.size
        s = self.scale
        x0 = max(0, int(math.floor(-self.ox / s)))
        y0 = max(0, int(math.floor(-self.oy / s)))
        x1 = min(w, int(math.ceil((cw - self.ox) / s)))
        y1 = min(h, int(math.ceil((ch - self.oy) / s)))
        if x1 <= x0 or y1 <= y0:
            return
        region = self.base.crop((x0, y0, x1, y1))
        tw = max(1, int(round((x1 - x0) * s)))
        th = max(1, int(round((y1 - y0) * s)))
        if (tw, th) != region.size:
            if s >= 3:
                method = Image.NEAREST
            elif s < 1:
                method = Image.LANCZOS
            else:
                method = Image.BILINEAR
            region = region.resize((tw, th), method)
        self.tkimg = ImageTk.PhotoImage(region)
        c.create_image(self.ox + x0 * s, self.oy + y0 * s, anchor="nw",
                       image=self.tkimg, tags="img")
        for color, wimg, pts in self.strokes:
            self.draw_stroke_canvas(color, wimg, pts)

    def draw_stroke_canvas(self, color, wimg, pts):
        s = self.scale
        wid = max(1.0, wimg * s)
        if len(pts) < 4:
            x, y = self.ox + pts[0] * s, self.oy + pts[1] * s
            r = wid / 2
            self.canvas.create_oval(x - r, y - r, x + r, y + r, fill=color,
                                    outline=color, tags="stroke")
            return
        flat = []
        for i in range(0, len(pts), 2):
            flat += [self.ox + pts[i] * s, self.oy + pts[i + 1] * s]
        self.canvas.create_line(*flat, fill=color, width=wid, capstyle="round",
                                joinstyle="round", tags="stroke")

    # ------------------------------------------------------ mouse: pan/draw
    def to_img(self, x, y):
        return (x - self.ox) / self.scale, (y - self.oy) / self.scale

    def on_press(self, e):
        if self.pen_on and not self.animated:
            ix, iy = self.to_img(e.x, e.y)
            w, h = self.base.size
            if not (0 <= ix <= w and 0 <= iy <= h):
                return
            self.cur_stroke = (self.pen_color, PEN_SCREEN_WIDTH / self.scale, [ix, iy])
            self.strokes.append(self.cur_stroke)
            self.dirty = True
            self.draw_stroke_canvas(*self.cur_stroke)
            self.last_xy = (e.x, e.y)
        else:
            self.on_pan_start(e)

    def on_drag(self, e):
        if self.cur_stroke is not None:
            color, wimg, pts = self.cur_stroke
            ix, iy = self.to_img(e.x, e.y)
            pts += [ix, iy]
            lx, ly = self.last_xy
            self.canvas.create_line(lx, ly, e.x, e.y, fill=color,
                                    width=max(1.0, wimg * self.scale),
                                    capstyle="round", tags="stroke")
            self.last_xy = (e.x, e.y)
        elif self.pan_last:
            self.on_pan_move(e)

    def on_release(self, _e):
        self.cur_stroke = None
        self.pan_last = None

    def on_pan_start(self, e):
        self.pan_last = (e.x, e.y)

    def on_pan_move(self, e):
        if not self.pan_last:
            return
        self.ox += e.x - self.pan_last[0]
        self.oy += e.y - self.pan_last[1]
        self.pan_last = (e.x, e.y)
        self.fit_mode = False
        self.render()

    # ---------------------------------------------------------------- pen
    def toggle_pen(self):
        self.set_pen(not self.pen_on)

    def set_pen(self, on):
        if on and self.animated:
            self.say("القلم ما يشتغل على الصور المتحركة")
            return
        self.pen_on = on
        self.refresh_pen_ui()

    def set_color(self, hexv):
        self.pen_color = hexv
        self.refresh_pen_ui()

    def refresh_pen_ui(self):
        t = THEMES[self.theme_name]
        self.pen_btn.configure(state="disabled" if self.animated else "normal")
        self.pen_btn.configure(bg=t["hover"] if self.pen_on else t["btn"])
        self.canvas.configure(cursor="pencil" if self.pen_on else "")
        self.save_btn.pack_forget()
        self.colors.pack_forget()
        if self.pen_on and not self.animated:
            self.save_btn.pack(side="right", padx=(0, 3))
            self.colors.pack(side="right", padx=(0, 6))
        for hexv, b in self.swatches.items():
            sel = hexv == self.pen_color
            b.configure(highlightthickness=2 if sel else 1,
                        highlightbackground=t["fg"] if sel else t["border"],
                        highlightcolor=t["fg"] if sel else t["border"])

    def undo(self):
        if self.strokes:
            self.strokes.pop()
            self.dirty = bool(self.strokes)
            self.render()

    # --------------------------------------------------------- flip / save
    def flip(self):
        w = self.base.size[0]
        self.frames = [ImageOps.mirror(f) for f in self.frames]
        self.base = self.frames[self.frame_i]
        for i, (color, wimg, pts) in enumerate(self.strokes):
            for j in range(0, len(pts), 2):
                pts[j] = w - pts[j]
        self.render()

    def save(self):
        if not self.strokes:
            self.say("ما في تعديل للحفظ")
            return False
        path = self.files[self.idx]
        stem, ext = os.path.splitext(path)
        out = f"{stem}_edited{ext}"
        n = 2
        while os.path.exists(out):  # ما نكتب فوق أي ملف
            out = f"{stem}_edited{n}{ext}"
            n += 1
        img = self.base.copy()
        d = ImageDraw.Draw(img)
        for color, wimg, pts in self.strokes:
            wi = max(1, int(round(wimg)))
            r = wi / 2
            if len(pts) < 4:
                x, y = pts[0], pts[1]
                d.ellipse((x - r, y - r, x + r, y + r), fill=color)
                continue
            d.line(pts, fill=color, width=wi, joint="curve")
            for i in (0, len(pts) - 2):
                x, y = pts[i], pts[i + 1]
                d.ellipse((x - r, y - r, x + r, y + r), fill=color)
        try:
            if ext.lower() in (".jpg", ".jpeg", ".bmp", ".jfif"):
                img = img.convert("RGB")
            img.save(out)
        except Exception:  # noqa: BLE001  (صيغة ما تنحفظ) نحفظ PNG
            out = os.path.splitext(out)[0] + ".png"
            try:
                img.save(out, "PNG")
            except Exception as exc:  # noqa: BLE001
                messagebox.showerror("zat", f"فشل الحفظ:\n{exc}")
                return False
        self.dirty = False
        if out not in self.files:
            self.files.append(out)
            self.files.sort(key=lambda p: natural_key(os.path.basename(p)))
            self.idx = self.files.index(path)
        self.say("انحفظت: " + os.path.basename(out), 4000)
        return True

    def confirm_discard(self):
        if not self.dirty:
            return True
        ans = messagebox.askyesnocancel("zat", "في تعديلات ما انحفظت. تحفظها؟")
        if ans is None:
            return False
        if ans:
            return self.save()
        return True

    def on_close(self):
        if self.confirm_discard():
            self.stop_anim()
            self.root.destroy()


def main():
    try:  # حدة أفضل على شاشات 4K
        import ctypes
        ctypes.windll.shcore.SetProcessDpiAwareness(1)
    except Exception:  # noqa: BLE001
        pass

    root = tk.Tk()
    path = sys.argv[1] if len(sys.argv) > 1 else ""
    if not path or not is_image_file(path):
        root.withdraw()
        messagebox.showinfo(
            "zat",
            "zat قارئ صور بس.\nيشتغل لما تفتح صورة فيه:\n"
            "كليك يمين على الصورة ← فتح بواسطة ← zat",
        )
        root.destroy()
        return
    Viewer(root, path)
    root.mainloop()


if __name__ == "__main__":
    main()
