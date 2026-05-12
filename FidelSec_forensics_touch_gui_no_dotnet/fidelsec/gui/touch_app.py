#!/usr/bin/env python3
import queue
import threading
from datetime import datetime
from pathlib import Path
import tkinter as tk
from tkinter import filedialog, messagebox

from fidelsec.core.devices import list_devices, size_bytes, is_root
from fidelsec.core.hash_utils import hash_file, write_hash_report
from fidelsec.core.imager import create_raw_image
from fidelsec.core.logger import CaseLogger
from fidelsec.core.verify import verify_image


BASE_DIR = Path(__file__).resolve().parents[2]
OUTPUT_DIR = BASE_DIR / "output"
LOG_DIR = BASE_DIR / "logs"

COL = {
    "bg": "#07111f",
    "panel": "#0f1b2d",
    "card": "#14243a",
    "card2": "#1c314f",
    "accent": "#2dd4bf",
    "accent2": "#38bdf8",
    "ok": "#22c55e",
    "danger": "#ef4444",
    "warn": "#f59e0b",
    "text": "#f8fafc",
    "muted": "#a7b4c6",
    "input": "#020617",
    "border": "#2e466b",
}


class TouchButton(tk.Button):
    def __init__(self, parent, text, command=None, kind="normal", **kw):
        colors = {
            "normal": (COL["card2"], COL["text"]),
            "primary": (COL["accent"], "#022c22"),
            "blue": (COL["accent2"], "#082f49"),
            "danger": (COL["danger"], "#ffffff"),
            "ok": (COL["ok"], "#052e16"),
            "warn": (COL["warn"], "#451a03"),
        }
        bg, fg = colors.get(kind, colors["normal"])
        super().__init__(
            parent,
            text=text,
            command=command,
            bg=bg,
            fg=fg,
            activebackground=bg,
            activeforeground=fg,
            relief="flat",
            bd=0,
            padx=22,
            pady=18,
            font=("DejaVu Sans", 16, "bold"),
            cursor="hand2",
            **kw
        )


class TouchEntry(tk.Entry):
    def __init__(self, parent, var):
        super().__init__(
            parent,
            textvariable=var,
            bg=COL["input"],
            fg=COL["text"],
            insertbackground=COL["text"],
            relief="flat",
            highlightthickness=2,
            highlightbackground=COL["border"],
            highlightcolor=COL["accent2"],
            font=("DejaVu Sans", 17),
        )


class TouchApp(tk.Tk):
    def __init__(self):
        super().__init__()

        self.title("FidelSec Touch")
        self.geometry("1280x800")
        self.minsize(1024, 600)
        self.configure(bg=COL["bg"])

        self.fullscreen = False
        self.devices = []
        self.selected_index = None
        self.selected_device = None
        self.total_bytes = 0
        self.stop_requested = False
        self.q = queue.Queue()

        self.case_var = tk.StringVar(value="Case")
        self.examiner_var = tk.StringVar(value="")
        self.output_var = tk.StringVar(value=str(OUTPUT_DIR))
        self.name_var = tk.StringVar(value="")
        self.block_var = tk.StringVar(value="4M")
        self.hash_sha256 = tk.BooleanVar(value=True)
        self.hash_sha1 = tk.BooleanVar(value=False)
        self.hash_md5 = tk.BooleanVar(value=False)

        self.verify_image_var = tk.StringVar()
        self.verify_report_var = tk.StringVar()

        OUTPUT_DIR.mkdir(exist_ok=True)
        LOG_DIR.mkdir(exist_ok=True)

        self.bind("<F11>", lambda e: self.toggle_fullscreen())
        self.bind("<Escape>", lambda e: self.exit_fullscreen())

        self.build()
        self.show_home()
        self.refresh_devices()
        self.after(100, self.drain)

    def build(self):
        self.header = tk.Frame(self, bg=COL["panel"], height=82)
        self.header.pack(fill="x")
        self.header.pack_propagate(False)

        tk.Label(
            self.header,
            text="FidelSec Touch",
            bg=COL["panel"],
            fg=COL["text"],
            font=("DejaVu Sans", 28, "bold"),
        ).pack(side="left", padx=24)

        self.status = tk.Label(
            self.header,
            text="READY",
            bg=COL["card"],
            fg=COL["muted"],
            font=("DejaVu Sans", 15, "bold"),
            padx=22,
            pady=12,
        )
        self.status.pack(side="right", padx=22)

        root_text = "ROOT OK" if is_root() else "START MET SUDO"
        root_col = COL["ok"] if is_root() else COL["danger"]
        self.root_badge = tk.Label(
            self.header,
            text=root_text,
            bg=root_col,
            fg="#ffffff",
            font=("DejaVu Sans", 14, "bold"),
            padx=18,
            pady=12,
        )
        self.root_badge.pack(side="right", padx=8)

        self.body = tk.Frame(self, bg=COL["bg"])
        self.body.pack(fill="both", expand=True)

    def clear(self):
        for w in self.body.winfo_children():
            w.destroy()

    def nav(self, parent):
        bar = tk.Frame(parent, bg=COL["bg"])
        bar.pack(fill="x", pady=(0, 18))
        TouchButton(bar, "🏠 Home", self.show_home).pack(side="left", padx=(0, 10))
        TouchButton(bar, "💾 Imaging", self.show_imaging, "primary").pack(side="left", padx=10)
        TouchButton(bar, "✅ Verify", self.show_verify, "blue").pack(side="left", padx=10)
        TouchButton(bar, "📜 Logs", self.show_logs).pack(side="left", padx=10)
        TouchButton(bar, "⛶ Fullscreen", self.toggle_fullscreen).pack(side="right")

    def page_title(self, parent, title, subtitle=""):
        tk.Label(parent, text=title, bg=COL["bg"], fg=COL["text"], font=("DejaVu Sans", 30, "bold")).pack(anchor="w")
        if subtitle:
            tk.Label(parent, text=subtitle, bg=COL["bg"], fg=COL["muted"], font=("DejaVu Sans", 17)).pack(anchor="w", pady=(4, 18))

    def card(self, parent, title=None):
        outer = tk.Frame(parent, bg=COL["card"], highlightbackground=COL["border"], highlightthickness=2)
        if title:
            tk.Label(outer, text=title, bg=COL["card"], fg=COL["accent"], font=("DejaVu Sans", 18, "bold")).pack(anchor="w", padx=20, pady=(16, 6))
        inner = tk.Frame(outer, bg=COL["card"])
        inner.pack(fill="both", expand=True, padx=20, pady=(10, 20))
        return outer, inner

    def show_home(self):
        self.clear()
        p = tk.Frame(self.body, bg=COL["bg"], padx=24, pady=22)
        p.pack(fill="both", expand=True)
        self.nav(p)
        self.page_title(p, "Touchscreen Dashboard", "Grote knoppen, eenvoudige workflow, geschikt voor Raspberry Pi touchscreens.")

        grid = tk.Frame(p, bg=COL["bg"])
        grid.pack(fill="x", pady=10)
        grid.columnconfigure((0, 1, 2), weight=1)

        for i, (title, value, sub) in enumerate([
            ("Disks", str(len(self.devices)), "gedetecteerd"),
            ("Root", "OK" if is_root() else "Nee", "sudo nodig"),
            ("Output", "Ready", "raw image + hashes"),
        ]):
            c, b = self.card(grid)
            c.grid(row=0, column=i, sticky="ew", padx=8)
            tk.Label(b, text=title, bg=COL["card"], fg=COL["muted"], font=("DejaVu Sans", 16)).pack(anchor="w")
            tk.Label(b, text=value, bg=COL["card"], fg=COL["text"], font=("DejaVu Sans", 32, "bold")).pack(anchor="w", pady=6)
            tk.Label(b, text=sub, bg=COL["card"], fg=COL["muted"], font=("DejaVu Sans", 15)).pack(anchor="w")

        actions, a = self.card(p, "Start")
        actions.pack(fill="x", pady=22)
        TouchButton(a, "1  Kies disk en start imaging", self.show_imaging, "primary").pack(side="left", fill="x", expand=True, padx=8)
        TouchButton(a, "2  Verifieer image", self.show_verify, "blue").pack(side="left", fill="x", expand=True, padx=8)
        TouchButton(a, "3  Bekijk logs", self.show_logs).pack(side="left", fill="x", expand=True, padx=8)

        c, b = self.card(p, "Activiteit")
        c.pack(fill="both", expand=True)
        self.activity = tk.Text(b, bg=COL["input"], fg=COL["text"], relief="flat", font=("DejaVu Sans Mono", 14), wrap="word")
        self.activity.pack(fill="both", expand=True)

    def show_imaging(self):
        self.clear()
        p = tk.Frame(self.body, bg=COL["bg"], padx=24, pady=22)
        p.pack(fill="both", expand=True)
        self.nav(p)
        self.page_title(p, "Disk Imaging", "Selecteer met grote touch-kaarten een bron-disk en maak een bit-exact image.")

        main = tk.Frame(p, bg=COL["bg"])
        main.pack(fill="both", expand=True)
        main.columnconfigure(0, weight=1)
        main.columnconfigure(1, weight=1)
        main.rowconfigure(0, weight=1)

        left, lb = self.card(main, "1. Bron-disk")
        left.grid(row=0, column=0, sticky="nsew", padx=(0, 10))

        TouchButton(lb, "🔄 Disks verversen", self.refresh_devices, "blue").pack(fill="x", pady=(0, 12))

        self.disk_list_frame = tk.Frame(lb, bg=COL["card"])
        self.disk_list_frame.pack(fill="both", expand=True)
        self.render_disk_buttons()

        right, rb = self.card(main, "2. Zaak en output")
        right.grid(row=0, column=1, sticky="nsew", padx=(10, 0))
        right.columnconfigure(0, weight=1)

        self.form_row(rb, "Case", self.case_var)
        self.form_row(rb, "Examiner", self.examiner_var)
        self.form_row(rb, "Output folder", self.output_var, button=("Kies", self.choose_output))
        self.form_row(rb, "Image name", self.name_var)
        self.form_row(rb, "Block size", self.block_var)

        hash_box = tk.Frame(rb, bg=COL["card"])
        hash_box.pack(fill="x", pady=14)
        tk.Label(hash_box, text="Hashes", bg=COL["card"], fg=COL["muted"], font=("DejaVu Sans", 15, "bold")).pack(anchor="w")
        for text, var in [("SHA256", self.hash_sha256), ("SHA1", self.hash_sha1), ("MD5", self.hash_md5)]:
            cb = tk.Checkbutton(
                hash_box,
                text=text,
                variable=var,
                bg=COL["card"],
                fg=COL["text"],
                activebackground=COL["card"],
                activeforeground=COL["text"],
                selectcolor=COL["input"],
                font=("DejaVu Sans", 18, "bold"),
                padx=18,
                pady=10,
            )
            cb.pack(side="left", padx=(0, 12), pady=8)

        self.progress = tk.Scale(
            rb,
            from_=0,
            to=100,
            orient="horizontal",
            bg=COL["card"],
            fg=COL["text"],
            troughcolor=COL["input"],
            highlightthickness=0,
            state="disabled",
            length=520,
            font=("DejaVu Sans", 12),
        )
        self.progress.pack(fill="x", pady=8)
        self.progress_text = tk.Label(rb, text="", bg=COL["card"], fg=COL["muted"], font=("DejaVu Sans", 16))
        self.progress_text.pack(anchor="w", pady=(0, 14))

        controls = tk.Frame(rb, bg=COL["card"])
        controls.pack(fill="x", pady=(8, 0))
        self.start_btn = TouchButton(controls, "▶ START IMAGE", self.start_imaging, "primary")
        self.start_btn.pack(side="left", fill="x", expand=True, padx=(0, 8))
        self.cancel_btn = TouchButton(controls, "■ STOP", self.cancel, "danger")
        self.cancel_btn.pack(side="left", fill="x", expand=True, padx=(8, 0))
        self.cancel_btn.configure(state="disabled")

    def form_row(self, parent, label, var, button=None):
        row = tk.Frame(parent, bg=COL["card"])
        row.pack(fill="x", pady=8)
        tk.Label(row, text=label, bg=COL["card"], fg=COL["muted"], font=("DejaVu Sans", 15, "bold")).pack(anchor="w")
        line = tk.Frame(row, bg=COL["card"])
        line.pack(fill="x", pady=(4, 0))
        ent = TouchEntry(line, var)
        ent.pack(side="left", fill="x", expand=True, ipady=9)
        if button:
            text, cmd = button
            TouchButton(line, text, cmd, "blue").pack(side="left", padx=(10, 0))

    def render_disk_buttons(self):
        if not hasattr(self, "disk_list_frame"):
            return
        for w in self.disk_list_frame.winfo_children():
            w.destroy()

        if not self.devices:
            tk.Label(self.disk_list_frame, text="Geen disks gevonden", bg=COL["card"], fg=COL["muted"], font=("DejaVu Sans", 18)).pack(anchor="w", pady=20)
            return

        for i, dev in enumerate(self.devices):
            selected = self.selected_index == i
            bg = COL["accent2"] if selected else COL["card2"]
            fg = "#082f49" if selected else COL["text"]
            btn = tk.Button(
                self.disk_list_frame,
                text=dev.label,
                justify="left",
                anchor="w",
                command=lambda idx=i: self.select_disk(idx),
                bg=bg,
                fg=fg,
                activebackground=bg,
                activeforeground=fg,
                relief="flat",
                bd=0,
                padx=18,
                pady=18,
                font=("DejaVu Sans", 16, "bold"),
                wraplength=470,
            )
            btn.pack(fill="x", pady=8)

    def select_disk(self, idx):
        self.selected_index = idx
        self.selected_device = self.devices[idx]
        self.name_var.set(f"fidelsec_{self.selected_device.name}_{datetime.now().strftime('%Y%m%d_%H%M%S')}")
        try:
            self.total_bytes = size_bytes(self.selected_device.path)
        except Exception:
            self.total_bytes = 0
        self.render_disk_buttons()
        self.set_status(f"Selected {self.selected_device.path}")

    def show_verify(self):
        self.clear()
        p = tk.Frame(self.body, bg=COL["bg"], padx=24, pady=22)
        p.pack(fill="both", expand=True)
        self.nav(p)
        self.page_title(p, "Verify Image", "Controleer of de hashes nog kloppen.")

        c, b = self.card(p, "Bestanden")
        c.pack(fill="x")
        self.form_row(b, "Image file", self.verify_image_var, button=("Kies", self.choose_verify_image))
        self.form_row(b, "Hash report", self.verify_report_var, button=("Kies", self.choose_verify_report))
        TouchButton(b, "✅ START VERIFY", self.start_verify, "ok").pack(fill="x", pady=16)

        c, b = self.card(p, "Resultaat")
        c.pack(fill="both", expand=True, pady=16)
        self.verify_result = tk.Text(b, bg=COL["input"], fg=COL["text"], relief="flat", font=("DejaVu Sans Mono", 15), wrap="word")
        self.verify_result.pack(fill="both", expand=True)

    def show_logs(self):
        self.clear()
        p = tk.Frame(self.body, bg=COL["bg"], padx=24, pady=22)
        p.pack(fill="both", expand=True)
        self.nav(p)
        self.page_title(p, "Logs", "Touch-vriendelijke logviewer.")

        top = tk.Frame(p, bg=COL["bg"])
        top.pack(fill="x", pady=(0, 12))
        TouchButton(top, "🔄 Ververs", self.refresh_logs, "blue").pack(side="left")
        TouchButton(top, "📖 Open geselecteerde log", self.open_log, "primary").pack(side="left", padx=12)

        split = tk.Frame(p, bg=COL["bg"])
        split.pack(fill="both", expand=True)
        split.columnconfigure(1, weight=1)
        split.rowconfigure(0, weight=1)

        self.log_list = tk.Listbox(split, bg=COL["input"], fg=COL["text"], selectbackground=COL["accent2"], selectforeground="#082f49", relief="flat", font=("DejaVu Sans", 18), width=32)
        self.log_list.grid(row=0, column=0, sticky="ns")
        self.log_text = tk.Text(split, bg=COL["input"], fg=COL["text"], relief="flat", font=("DejaVu Sans Mono", 15), wrap="word")
        self.log_text.grid(row=0, column=1, sticky="nsew", padx=(14, 0))
        self.refresh_logs()

    def refresh_devices(self):
        try:
            self.devices = list_devices()
            if self.selected_index is None and self.devices:
                self.select_disk(0)
            self.render_disk_buttons()
            self.log(f"{len(self.devices)} disk(s) gevonden")
        except Exception as exc:
            messagebox.showerror("Disk fout", str(exc))

    def choose_output(self):
        folder = filedialog.askdirectory(initialdir=self.output_var.get())
        if folder:
            self.output_var.set(folder)

    def algorithms(self):
        algos = []
        if self.hash_sha256.get():
            algos.append("sha256")
        if self.hash_sha1.get():
            algos.append("sha1")
        if self.hash_md5.get():
            algos.append("md5")
        return algos

    def start_imaging(self):
        if not is_root():
            messagebox.showerror("Root nodig", "Start met:\n\nsudo ./run-touch.sh\n\nof:\nsudo fidelsec-touch")
            return
        if not self.selected_device:
            messagebox.showwarning("Geen disk", "Kies eerst een bron-disk.")
            return
        algos = self.algorithms()
        if not algos:
            messagebox.showwarning("Geen hash", "Kies minimaal SHA256, SHA1 of MD5.")
            return

        out = Path(self.output_var.get()).expanduser()
        out.mkdir(parents=True, exist_ok=True)
        name = self.name_var.get().strip() or f"fidelsec_{datetime.now().strftime('%Y%m%d_%H%M%S')}"
        if not name.endswith(".img"):
            name += ".img"
        image = out / name

        if not messagebox.askyesno("Bevestigen", f"Bron:\n{self.selected_device.path}\n\nOutput:\n{image}\n\nStart imaging?"):
            return

        self.stop_requested = False
        self.start_btn.configure(state="disabled")
        self.cancel_btn.configure(state="normal")
        self.set_status("IMAGING")
        threading.Thread(target=self.worker_image, args=(image, algos), daemon=True).start()

    def worker_image(self, image, algos):
        log_path = LOG_DIR / f"fidelsec_touch_{datetime.now().strftime('%Y%m%d_%H%M%S')}.log"
        logger = CaseLogger(log_path)
        try:
            logger.start(self.case_var.get(), self.examiner_var.get(), self.selected_device.path, str(image))
            total = self.total_bytes or size_bytes(self.selected_device.path)
            self.log("Imaging gestart")

            def progress(copied, speed):
                self.q.put(("progress", (copied, total, speed)))

            copied = create_raw_image(
                self.selected_device.path,
                image,
                self.block_var.get(),
                progress=progress,
                should_stop=lambda: self.stop_requested,
            )
            logger.append(f"Copied bytes: {copied}")
            self.log("Hashes berekenen")
            hashes = hash_file(image, algos)
            report = write_hash_report(image, hashes)
            for algo, digest in hashes.items():
                logger.append(f"{algo.upper()}: {digest}")
            logger.append(f"Hash report: {report}")
            logger.append("Completed")

            self.verify_image_var.set(str(image))
            self.verify_report_var.set(str(report))
            self.q.put(("done", f"Image klaar:\n{image}\n\nHash report:\n{report}\n\nLog:\n{log_path}"))
        except Exception as exc:
            try:
                logger.append(f"ERROR: {exc}")
            except Exception:
                pass
            self.q.put(("error", str(exc)))

    def cancel(self):
        self.stop_requested = True
        self.log("Stop aangevraagd")

    def choose_verify_image(self):
        path = filedialog.askopenfilename(filetypes=[("Raw images", "*.img"), ("All files", "*")])
        if path:
            self.verify_image_var.set(path)

    def choose_verify_report(self):
        path = filedialog.askopenfilename(filetypes=[("Hash reports", "*.txt *.sha256"), ("All files", "*")])
        if path:
            self.verify_report_var.set(path)

    def start_verify(self):
        image = Path(self.verify_image_var.get())
        report = Path(self.verify_report_var.get())
        if not image.exists() or not report.exists():
            messagebox.showerror("Mist bestand", "Image of hash report bestaat niet.")
            return
        self.verify_result.delete("1.0", "end")
        self.verify_result.insert("end", "Verificatie bezig...\n")
        threading.Thread(target=self.worker_verify, args=(image, report), daemon=True).start()

    def worker_verify(self, image, report):
        try:
            result = verify_image(image, report)
            ok_all = True
            lines = []
            for algo, data in result.items():
                ok_all = ok_all and data["ok"]
                lines.append(f"{algo.upper()}: {'OK' if data['ok'] else 'FOUT'}")
                lines.append(f"Expected: {data['expected']}")
                lines.append(f"Actual:   {data['actual']}")
                lines.append("")
            self.verify_result.delete("1.0", "end")
            self.verify_result.insert("end", "\n".join(lines))
            messagebox.showinfo("Verify", "Alle hashes OK." if ok_all else "Hash mismatch gevonden.")
        except Exception as exc:
            messagebox.showerror("Verify fout", str(exc))

    def refresh_logs(self):
        if not hasattr(self, "log_list"):
            return
        self.log_list.delete(0, "end")
        for p in sorted(LOG_DIR.glob("*.log"), key=lambda x: x.stat().st_mtime, reverse=True):
            self.log_list.insert("end", p.name)

    def open_log(self):
        sel = self.log_list.curselection()
        if not sel:
            return
        path = LOG_DIR / self.log_list.get(sel[0])
        self.log_text.delete("1.0", "end")
        self.log_text.insert("end", path.read_text(errors="replace"))

    def drain(self):
        while True:
            try:
                kind, payload = self.q.get_nowait()
            except queue.Empty:
                break
            if kind == "progress":
                copied, total, speed = payload
                pct = min((copied / total) * 100, 100) if total else 0
                if hasattr(self, "progress"):
                    self.progress.set(pct)
                    self.progress_text.configure(text=f"{pct:.1f}%  •  {copied/(1024**2):.1f} MiB  •  {speed/(1024**2):.1f} MiB/s")
            elif kind == "done":
                if hasattr(self, "start_btn"):
                    self.start_btn.configure(state="normal")
                    self.cancel_btn.configure(state="disabled")
                self.set_status("READY")
                messagebox.showinfo("Klaar", payload)
            elif kind == "error":
                if hasattr(self, "start_btn"):
                    self.start_btn.configure(state="normal")
                    self.cancel_btn.configure(state="disabled")
                self.set_status("ERROR")
                messagebox.showerror("Fout", payload)
            elif kind == "log":
                self.log(payload)
        self.after(100, self.drain)

    def log(self, text):
        self.set_status(text)
        if hasattr(self, "activity"):
            self.activity.insert("end", f"[{datetime.now().strftime('%H:%M:%S')}] {text}\n")
            self.activity.see("end")

    def set_status(self, text):
        self.status.configure(text=text.upper())

    def toggle_fullscreen(self):
        self.fullscreen = not self.fullscreen
        self.attributes("-fullscreen", self.fullscreen)

    def exit_fullscreen(self):
        self.fullscreen = False
        self.attributes("-fullscreen", False)


if __name__ == "__main__":
    app = TouchApp()
    app.mainloop()
