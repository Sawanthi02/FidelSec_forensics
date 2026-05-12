#!/usr/bin/env python3
import queue
import threading
from datetime import datetime
from pathlib import Path
import tkinter as tk
from tkinter import ttk, filedialog, messagebox

from fidelsec.core.devices import list_devices, size_bytes, is_root
from fidelsec.core.hash_utils import SUPPORTED_HASHES, hash_file, write_hash_report
from fidelsec.core.imager import create_raw_image
from fidelsec.core.logger import CaseLogger
from fidelsec.core.verify import verify_image


BASE_DIR = Path(__file__).resolve().parents[2]
OUTPUT_DIR = BASE_DIR / "output"
LOG_DIR = BASE_DIR / "logs"


COLORS = {
    "bg": "#0f172a",
    "panel": "#111827",
    "panel2": "#1f2937",
    "card": "#172033",
    "accent": "#38bdf8",
    "accent2": "#22c55e",
    "danger": "#ef4444",
    "text": "#e5e7eb",
    "muted": "#94a3b8",
    "border": "#334155",
    "input": "#020617",
}


class ModernFidelSec(tk.Tk):
    def __init__(self):
        super().__init__()

        self.title("FidelSec Forensic Imager")
        self.geometry("1180x760")
        self.minsize(1050, 680)
        self.configure(bg=COLORS["bg"])

        self.devices = []
        self.selected_device = None
        self.total_bytes = 0
        self.stop_requested = False
        self.q = queue.Queue()
        self.latest_image = None
        self.latest_hash = None

        OUTPUT_DIR.mkdir(exist_ok=True)
        LOG_DIR.mkdir(exist_ok=True)

        self._setup_styles()
        self._layout()
        self.refresh_devices()
        self.after(120, self._drain_queue)

    def _setup_styles(self):
        style = ttk.Style(self)
        try:
            style.theme_use("clam")
        except tk.TclError:
            pass

        style.configure(".", background=COLORS["bg"], foreground=COLORS["text"], font=("Segoe UI", 10))
        style.configure("Sidebar.TFrame", background=COLORS["panel"])
        style.configure("Main.TFrame", background=COLORS["bg"])
        style.configure("Card.TFrame", background=COLORS["card"], relief="flat")
        style.configure("Panel.TLabelframe", background=COLORS["bg"], foreground=COLORS["text"], bordercolor=COLORS["border"])
        style.configure("Panel.TLabelframe.Label", background=COLORS["bg"], foreground=COLORS["accent"], font=("Segoe UI", 10, "bold"))

        style.configure("TLabel", background=COLORS["bg"], foreground=COLORS["text"])
        style.configure("Muted.TLabel", background=COLORS["bg"], foreground=COLORS["muted"])
        style.configure("Hero.TLabel", background=COLORS["bg"], foreground=COLORS["text"], font=("Segoe UI", 24, "bold"))
        style.configure("Brand.TLabel", background=COLORS["panel"], foreground=COLORS["text"], font=("Segoe UI", 22, "bold"))
        style.configure("SideMuted.TLabel", background=COLORS["panel"], foreground=COLORS["muted"])

        style.configure("TButton", background=COLORS["panel2"], foreground=COLORS["text"], borderwidth=0, padding=(14, 9))
        style.map("TButton", background=[("active", COLORS["border"])])

        style.configure("Primary.TButton", background=COLORS["accent"], foreground="#00111c", padding=(16, 10), font=("Segoe UI", 10, "bold"))
        style.map("Primary.TButton", background=[("active", "#7dd3fc")])

        style.configure("Danger.TButton", background=COLORS["danger"], foreground="white", padding=(16, 10))
        style.map("Danger.TButton", background=[("active", "#f87171")])

        style.configure("Side.TButton", background=COLORS["panel"], foreground=COLORS["text"], padding=(14, 11), anchor="w")
        style.map("Side.TButton", background=[("active", COLORS["panel2"])])

        style.configure("TEntry", fieldbackground=COLORS["input"], foreground=COLORS["text"], insertcolor=COLORS["text"], bordercolor=COLORS["border"])
        style.configure("TCombobox", fieldbackground=COLORS["input"], foreground=COLORS["text"], arrowcolor=COLORS["accent"])
        style.configure("Horizontal.TProgressbar", background=COLORS["accent2"], troughcolor=COLORS["panel2"], bordercolor=COLORS["panel2"])

    def _layout(self):
        self.columnconfigure(1, weight=1)
        self.rowconfigure(0, weight=1)

        self.sidebar = ttk.Frame(self, style="Sidebar.TFrame", padding=(22, 24))
        self.sidebar.grid(row=0, column=0, sticky="ns")
        self.sidebar.grid_propagate(False)
        self.sidebar.configure(width=250)

        ttk.Label(self.sidebar, text="FidelSec", style="Brand.TLabel").pack(anchor="w")
        ttk.Label(self.sidebar, text="Forensic Imager", style="SideMuted.TLabel").pack(anchor="w", pady=(0, 26))

        self.nav_buttons = {}
        for label, page in [
            ("▣  Dashboard", "dashboard"),
            ("◉  Disk Imaging", "imaging"),
            ("◆  Hash & Verify", "verify"),
            ("☰  Logs", "logs"),
            ("⚙  Settings", "settings"),
            ("ⓘ  About", "about"),
        ]:
            btn = ttk.Button(self.sidebar, text=label, style="Side.TButton", command=lambda p=page: self.show_page(p))
            btn.pack(fill="x", pady=5)
            self.nav_buttons[page] = btn

        ttk.Label(self.sidebar, text="Runtime", style="SideMuted.TLabel").pack(anchor="w", pady=(32, 4))
        root_text = "ROOT OK" if is_root() else "NO ROOT"
        self.root_badge = tk.Label(
            self.sidebar, text=root_text,
            bg=("#064e3b" if is_root() else "#7f1d1d"),
            fg=("#bbf7d0" if is_root() else "#fecaca"),
            padx=12, pady=7, font=("Segoe UI", 9, "bold")
        )
        self.root_badge.pack(anchor="w")

        self.main = ttk.Frame(self, style="Main.TFrame", padding=(26, 22))
        self.main.grid(row=0, column=1, sticky="nsew")
        self.main.columnconfigure(0, weight=1)
        self.main.rowconfigure(1, weight=1)

        header = ttk.Frame(self.main, style="Main.TFrame")
        header.grid(row=0, column=0, sticky="ew", pady=(0, 18))
        header.columnconfigure(0, weight=1)

        self.title_label = ttk.Label(header, text="", style="Hero.TLabel")
        self.title_label.grid(row=0, column=0, sticky="w")

        self.status_label = tk.Label(
            header, text="Ready", bg=COLORS["card"], fg=COLORS["muted"],
            padx=14, pady=8, font=("Segoe UI", 10)
        )
        self.status_label.grid(row=0, column=1, sticky="e")

        self.page_host = ttk.Frame(self.main, style="Main.TFrame")
        self.page_host.grid(row=1, column=0, sticky="nsew")
        self.page_host.columnconfigure(0, weight=1)
        self.page_host.rowconfigure(0, weight=1)

        self.pages = {}
        self._build_dashboard()
        self._build_imaging()
        self._build_verify()
        self._build_logs()
        self._build_settings()
        self._build_about()
        self.show_page("dashboard")

    def page(self, name):
        frame = ttk.Frame(self.page_host, style="Main.TFrame")
        frame.grid(row=0, column=0, sticky="nsew")
        frame.columnconfigure(0, weight=1)
        self.pages[name] = frame
        return frame

    def card(self, parent, title=None):
        outer = tk.Frame(parent, bg=COLORS["card"], highlightbackground=COLORS["border"], highlightthickness=1)
        if title:
            tk.Label(outer, text=title, bg=COLORS["card"], fg=COLORS["accent"], font=("Segoe UI", 11, "bold")).pack(anchor="w", padx=16, pady=(14, 4))
        body = tk.Frame(outer, bg=COLORS["card"])
        body.pack(fill="both", expand=True, padx=16, pady=(8, 16))
        return outer, body

    def label(self, parent, text, muted=False, size=10, bold=False):
        return tk.Label(
            parent, text=text,
            bg=COLORS["card"],
            fg=COLORS["muted"] if muted else COLORS["text"],
            font=("Segoe UI", size, "bold" if bold else "normal")
        )

    def entry(self, parent, var):
        e = tk.Entry(parent, textvariable=var, bg=COLORS["input"], fg=COLORS["text"], insertbackground=COLORS["text"], relief="flat", highlightthickness=1, highlightbackground=COLORS["border"])
        return e

    def _build_dashboard(self):
        p = self.page("dashboard")

        grid = tk.Frame(p, bg=COLORS["bg"])
        grid.pack(fill="x")
        grid.columnconfigure((0, 1, 2), weight=1)

        self.disk_count_var = tk.StringVar(value="0")
        self.latest_var = tk.StringVar(value="No image yet")
        self.case_status_var = tk.StringVar(value="Idle")

        for i, (title, var, hint) in enumerate([
            ("Detected disks", self.disk_count_var, "Available physical drives"),
            ("Last image", self.latest_var, "Most recent acquisition"),
            ("Case status", self.case_status_var, "Current workflow state"),
        ]):
            c, b = self.card(grid)
            c.grid(row=0, column=i, sticky="ew", padx=(0 if i == 0 else 10, 0))
            self.label(b, title, muted=True).pack(anchor="w")
            self.label(b, "", muted=False).pack_forget()
            tk.Label(b, textvariable=var, bg=COLORS["card"], fg=COLORS["text"], font=("Segoe UI", 18, "bold")).pack(anchor="w", pady=(6, 2))
            self.label(b, hint, muted=True).pack(anchor="w")

        actions, body = self.card(p, "Quick actions")
        actions.pack(fill="x", pady=16)
        ttk.Button(body, text="Refresh disks", command=self.refresh_devices).pack(side="left")
        ttk.Button(body, text="Start acquisition", style="Primary.TButton", command=lambda: self.show_page("imaging")).pack(side="left", padx=10)
        ttk.Button(body, text="Verify image", command=lambda: self.show_page("verify")).pack(side="left")

        activity, ab = self.card(p, "Activity")
        activity.pack(fill="both", expand=True)
        self.activity = tk.Text(ab, bg=COLORS["input"], fg=COLORS["text"], insertbackground=COLORS["text"], relief="flat", height=14, wrap="word")
        self.activity.pack(fill="both", expand=True)

    def _build_imaging(self):
        p = self.page("imaging")

        c, b = self.card(p, "Source device")
        c.pack(fill="x")
        top = tk.Frame(b, bg=COLORS["card"])
        top.pack(fill="x")
        self.device_var = tk.StringVar()
        self.device_combo = ttk.Combobox(top, textvariable=self.device_var, state="readonly")
        self.device_combo.pack(side="left", fill="x", expand=True)
        self.device_combo.bind("<<ComboboxSelected>>", lambda e: self.on_device_selected())
        ttk.Button(top, text="Refresh", command=self.refresh_devices).pack(side="left", padx=(10, 0))
        self.device_meta = self.label(b, "No disk selected", muted=True)
        self.device_meta.pack(anchor="w", pady=(10, 0))

        c, b = self.card(p, "Case metadata")
        c.pack(fill="x", pady=14)
        form = tk.Frame(b, bg=COLORS["card"])
        form.pack(fill="x")
        form.columnconfigure(1, weight=1)
        form.columnconfigure(3, weight=1)

        self.case_var = tk.StringVar(value="Case")
        self.examiner_var = tk.StringVar(value="")
        self.label(form, "Case name", muted=True).grid(row=0, column=0, sticky="w", padx=(0, 8))
        self.entry(form, self.case_var).grid(row=0, column=1, sticky="ew", padx=(0, 16), ipady=7)
        self.label(form, "Examiner", muted=True).grid(row=0, column=2, sticky="w", padx=(0, 8))
        self.entry(form, self.examiner_var).grid(row=0, column=3, sticky="ew", ipady=7)

        c, b = self.card(p, "Acquisition settings")
        c.pack(fill="x")
        b.columnconfigure(1, weight=1)

        self.output_dir_var = tk.StringVar(value=str(OUTPUT_DIR))
        self.image_name_var = tk.StringVar(value="")
        self.block_size_var = tk.StringVar(value="4M")
        self.hash_vars = {}

        self.label(b, "Output folder", muted=True).grid(row=0, column=0, sticky="w", pady=5)
        self.entry(b, self.output_dir_var).grid(row=0, column=1, sticky="ew", padx=10, ipady=7)
        ttk.Button(b, text="Choose", command=self.choose_output).grid(row=0, column=2)

        self.label(b, "Image name", muted=True).grid(row=1, column=0, sticky="w", pady=5)
        self.entry(b, self.image_name_var).grid(row=1, column=1, sticky="ew", padx=10, ipady=7)
        self.label(b, ".img", muted=True).grid(row=1, column=2, sticky="w")

        self.label(b, "Block size", muted=True).grid(row=2, column=0, sticky="w", pady=5)
        self.entry(b, self.block_size_var).grid(row=2, column=1, sticky="w", padx=10, ipady=7)

        hash_frame = tk.Frame(b, bg=COLORS["card"])
        hash_frame.grid(row=3, column=1, sticky="w", padx=10, pady=8)
        for algo in SUPPORTED_HASHES:
            var = tk.BooleanVar(value=(algo == "sha256"))
            self.hash_vars[algo] = var
            cb = tk.Checkbutton(hash_frame, text=algo.upper(), variable=var, bg=COLORS["card"], fg=COLORS["text"], activebackground=COLORS["card"], selectcolor=COLORS["input"])
            cb.pack(side="left", padx=(0, 16))
        self.label(b, "Hashes", muted=True).grid(row=3, column=0, sticky="w")

        controls = tk.Frame(p, bg=COLORS["bg"])
        controls.pack(fill="x", pady=16)
        self.start_btn = ttk.Button(controls, text="Start bit-exact acquisition", style="Primary.TButton", command=self.start_imaging)
        self.start_btn.pack(side="left")
        self.cancel_btn = ttk.Button(controls, text="Cancel", style="Danger.TButton", state="disabled", command=self.cancel)
        self.cancel_btn.pack(side="left", padx=10)

        self.progress = ttk.Progressbar(p, maximum=100, mode="determinate")
        self.progress.pack(fill="x")
        self.progress_text = ttk.Label(p, text="", style="Muted.TLabel")
        self.progress_text.pack(anchor="w", pady=(8, 0))

    def _build_verify(self):
        p = self.page("verify")

        c, b = self.card(p, "Hash verification")
        c.pack(fill="x")
        b.columnconfigure(1, weight=1)

        self.verify_image_var = tk.StringVar()
        self.verify_report_var = tk.StringVar()

        self.label(b, "Image", muted=True).grid(row=0, column=0, sticky="w", pady=5)
        self.entry(b, self.verify_image_var).grid(row=0, column=1, sticky="ew", padx=10, ipady=7)
        ttk.Button(b, text="Choose", command=self.choose_verify_image).grid(row=0, column=2)

        self.label(b, "Hash report", muted=True).grid(row=1, column=0, sticky="w", pady=5)
        self.entry(b, self.verify_report_var).grid(row=1, column=1, sticky="ew", padx=10, ipady=7)
        ttk.Button(b, text="Choose", command=self.choose_verify_report).grid(row=1, column=2)

        ttk.Button(p, text="Run verification", style="Primary.TButton", command=self.start_verify).pack(anchor="w", pady=14)

        c, b = self.card(p, "Verification result")
        c.pack(fill="both", expand=True)
        self.verify_result = tk.Text(b, bg=COLORS["input"], fg=COLORS["text"], relief="flat", wrap="word")
        self.verify_result.pack(fill="both", expand=True)

    def _build_logs(self):
        p = self.page("logs")
        tools = tk.Frame(p, bg=COLORS["bg"])
        tools.pack(fill="x", pady=(0, 12))
        ttk.Button(tools, text="Refresh logs", command=self.refresh_logs).pack(side="left")
        ttk.Button(tools, text="Open selected", command=self.open_log).pack(side="left", padx=10)

        split = tk.Frame(p, bg=COLORS["bg"])
        split.pack(fill="both", expand=True)
        split.columnconfigure(1, weight=1)
        split.rowconfigure(0, weight=1)

        self.log_list = tk.Listbox(split, bg=COLORS["input"], fg=COLORS["text"], relief="flat", highlightthickness=1, highlightbackground=COLORS["border"], width=34)
        self.log_list.grid(row=0, column=0, sticky="ns")
        self.log_text = tk.Text(split, bg=COLORS["input"], fg=COLORS["text"], relief="flat", wrap="word")
        self.log_text.grid(row=0, column=1, sticky="nsew", padx=(12, 0))

    def _build_settings(self):
        p = self.page("settings")
        c, b = self.card(p, "Application settings")
        c.pack(fill="x")
        self.label(b, "Frontend style", muted=True).pack(anchor="w")
        self.label(b, "Modern dark forensic dashboard, Python/Tkinter, no .NET runtime.", size=12).pack(anchor="w", pady=(4, 14))
        self.label(b, "Default output folder", muted=True).pack(anchor="w")
        self.label(b, str(OUTPUT_DIR), size=12).pack(anchor="w", pady=(4, 0))

    def _build_about(self):
        p = self.page("about")
        c, b = self.card(p, "About FidelSec")
        c.pack(fill="both", expand=True)
        text = (
            "FidelSec Forensic Imager\\n\\n"
            "Modern native GUI rewrite of the .NET/Avalonia workflow.\\n"
            "Built for Ubuntu, Debian Bookworm and Raspberry Pi OS.\\n\\n"
            "Included functions:\\n"
            "• Dashboard with acquisition status\\n"
            "• Physical disk detection\\n"
            "• Bit-exact raw imaging\\n"
            "• Case metadata and examiner field\\n"
            "• MD5, SHA1 and SHA256 hashing\\n"
            "• Hash verification\\n"
            "• Forensic logging\\n"
            "• Logs viewer\\n\\n"
            "Runtime: Python 3 + Tkinter. No .NET, no Avalonia, no WPF."
        )
        self.label(b, text, size=12).pack(anchor="w")

    def show_page(self, name):
        titles = {
            "dashboard": "Dashboard",
            "imaging": "Disk Imaging",
            "verify": "Hash & Verify",
            "logs": "Case Logs",
            "settings": "Settings",
            "about": "About",
        }
        self.title_label.configure(text=titles[name])
        self.pages[name].tkraise()
        if name == "logs":
            self.refresh_logs()

    def set_status(self, text):
        self.status_label.configure(text=text)

    def log(self, text):
        self.q.put(("log", text))

    def _drain_queue(self):
        while True:
            try:
                kind, payload = self.q.get_nowait()
            except queue.Empty:
                break

            if kind == "log":
                line = f"[{datetime.now().strftime('%H:%M:%S')}] {payload}\\n"
                self.activity.insert("end", line)
                self.activity.see("end")
                self.set_status(payload)

            elif kind == "progress":
                copied, total, speed = payload
                pct = min((copied / total) * 100, 100) if total else 0
                self.progress["value"] = pct
                self.progress_text.configure(text=f"{pct:.1f}%  •  {copied/(1024**2):.1f} MiB copied  •  {speed/(1024**2):.1f} MiB/s")

            elif kind == "done":
                self.start_btn.configure(state="normal")
                self.cancel_btn.configure(state="disabled")
                self.case_status_var.set("Completed")
                messagebox.showinfo("Completed", payload)

            elif kind == "error":
                self.start_btn.configure(state="normal")
                self.cancel_btn.configure(state="disabled")
                self.case_status_var.set("Error")
                messagebox.showerror("Error", payload)

        self.after(120, self._drain_queue)

    def refresh_devices(self):
        try:
            self.devices = list_devices()
            self.device_combo["values"] = [d.label for d in self.devices]
            self.disk_count_var.set(str(len(self.devices)))
            if self.devices:
                self.device_combo.current(0)
                self.on_device_selected()
            self.log(f"{len(self.devices)} disk(s) detected")
        except Exception as e:
            messagebox.showerror("Device error", str(e))

    def on_device_selected(self):
        idx = self.device_combo.current()
        if idx < 0 or idx >= len(self.devices):
            return
        dev = self.devices[idx]
        self.selected_device = dev
        self.device_meta.configure(text=f"Serial: {dev.serial or 'unknown'}   Transport: {dev.tran or 'unknown'}   Mounted: {dev.mountpoints or 'no'}")
        self.image_name_var.set(f"fidelsec_{dev.name}_{datetime.now().strftime('%Y%m%d_%H%M%S')}")
        try:
            self.total_bytes = size_bytes(dev.path)
        except Exception:
            self.total_bytes = 0

    def choose_output(self):
        folder = filedialog.askdirectory(initialdir=self.output_dir_var.get())
        if folder:
            self.output_dir_var.set(folder)

    def selected_algorithms(self):
        return [a for a, v in self.hash_vars.items() if v.get()]

    def start_imaging(self):
        if not is_root():
            messagebox.showerror("Root required", "Start met sudo:\\n\\nsudo ./run-gui.sh\\n\\nof:\\nsudo fidelsec-gui")
            return
        if not self.selected_device:
            messagebox.showwarning("No source", "Selecteer eerst een bron-disk.")
            return
        algos = self.selected_algorithms()
        if not algos:
            messagebox.showwarning("No hash", "Selecteer minimaal één hash.")
            return

        out_dir = Path(self.output_dir_var.get()).expanduser()
        out_dir.mkdir(parents=True, exist_ok=True)
        name = self.image_name_var.get().strip() or f"fidelsec_{datetime.now().strftime('%Y%m%d_%H%M%S')}"
        if not name.endswith(".img"):
            name += ".img"
        image = out_dir / name

        msg = f"Source: {self.selected_device.path}\\nOutput: {image}\\n\\nStart bit-exact acquisition?"
        if not messagebox.askyesno("Confirm acquisition", msg):
            return

        self.stop_requested = False
        self.start_btn.configure(state="disabled")
        self.cancel_btn.configure(state="normal")
        self.progress["value"] = 0
        self.case_status_var.set("Imaging")

        threading.Thread(target=self._imaging_worker, args=(image, algos), daemon=True).start()

    def _imaging_worker(self, image, algos):
        log_path = LOG_DIR / f"fidelsec_{datetime.now().strftime('%Y%m%d_%H%M%S')}.log"
        logger = CaseLogger(log_path)
        try:
            logger.start(self.case_var.get(), self.examiner_var.get(), self.selected_device.path, str(image))
            self.log("Acquisition started")
            total = self.total_bytes or size_bytes(self.selected_device.path)

            def progress(copied, speed):
                self.q.put(("progress", (copied, total, speed)))

            copied = create_raw_image(
                self.selected_device.path,
                image,
                self.block_size_var.get(),
                progress=progress,
                should_stop=lambda: self.stop_requested,
            )
            logger.append(f"Copied bytes: {copied}")

            self.log("Calculating hashes")
            hashes = hash_file(image, algos)
            report = write_hash_report(image, hashes)

            for algo, digest in hashes.items():
                logger.append(f"{algo.upper()}: {digest}")
            logger.append(f"Hash report: {report}")
            logger.append("Completed")

            self.latest_image = image
            self.latest_hash = report
            self.latest_var.set(image.name)
            self.verify_image_var.set(str(image))
            self.verify_report_var.set(str(report))
            self.log("Acquisition completed")
            self.q.put(("done", f"Image created:\\n{image}\\n\\nHash report:\\n{report}\\n\\nLog:\\n{log_path}"))

        except Exception as e:
            try:
                logger.append(f"ERROR: {e}")
            except Exception:
                pass
            self.q.put(("error", str(e)))

    def cancel(self):
        self.stop_requested = True
        self.log("Cancel requested")

    def choose_verify_image(self):
        p = filedialog.askopenfilename(filetypes=[("Raw image", "*.img"), ("All files", "*")])
        if p:
            self.verify_image_var.set(p)

    def choose_verify_report(self):
        p = filedialog.askopenfilename(filetypes=[("Hash report", "*.txt *.sha256"), ("All files", "*")])
        if p:
            self.verify_report_var.set(p)

    def start_verify(self):
        image = Path(self.verify_image_var.get())
        report = Path(self.verify_report_var.get())
        if not image.exists() or not report.exists():
            messagebox.showerror("Missing file", "Image or hash report not found.")
            return
        self.verify_result.delete("1.0", "end")
        self.verify_result.insert("end", "Verification running...\\n")
        threading.Thread(target=self._verify_worker, args=(image, report), daemon=True).start()

    def _verify_worker(self, image, report):
        try:
            result = verify_image(image, report)
            lines = []
            ok_all = True
            for algo, data in result.items():
                ok_all = ok_all and data["ok"]
                lines.append(f"{algo.upper()}: {'OK' if data['ok'] else 'MISMATCH'}")
                lines.append(f"Expected: {data['expected']}")
                lines.append(f"Actual:   {data['actual']}")
                lines.append("")
            self.verify_result.delete("1.0", "end")
            self.verify_result.insert("end", "\\n".join(lines))
            messagebox.showinfo("Verification", "All hashes OK." if ok_all else "Hash mismatch found.")
        except Exception as e:
            messagebox.showerror("Verification error", str(e))

    def refresh_logs(self):
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


if __name__ == "__main__":
    app = ModernFidelSec()
    app.mainloop()
