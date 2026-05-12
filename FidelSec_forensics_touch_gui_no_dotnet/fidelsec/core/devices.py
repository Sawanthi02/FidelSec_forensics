import json
import os
import subprocess
from dataclasses import dataclass


@dataclass
class DiskDevice:
    name: str
    path: str
    size: str
    model: str
    serial: str
    tran: str
    removable: bool
    readonly: bool
    mountpoints: str

    @property
    def label(self):
        flags = []
        if self.removable:
            flags.append("USB")
        if self.readonly:
            flags.append("READ ONLY")
        if self.mountpoints:
            flags.append("MOUNTED")
        flag_text = f"  •  {' / '.join(flags)}" if flags else ""
        model = self.model or "Unknown disk"
        return f"{self.path}\n{self.size}  •  {model}{flag_text}"


def is_root():
    return os.geteuid() == 0


def run(args):
    return subprocess.run(args, text=True, capture_output=True, check=True)


def list_devices():
    result = run([
        "lsblk", "-J", "-d",
        "-o", "NAME,PATH,SIZE,MODEL,SERIAL,TRAN,RM,RO,MOUNTPOINTS,TYPE"
    ])
    payload = json.loads(result.stdout)
    devices = []
    for item in payload.get("blockdevices", []):
        if item.get("type") != "disk":
            continue
        name = item.get("name") or ""
        if name.startswith(("loop", "ram", "zram", "sr")):
            continue
        devices.append(DiskDevice(
            name=name,
            path=item.get("path") or f"/dev/{name}",
            size=str(item.get("size") or ""),
            model=str(item.get("model") or "").strip(),
            serial=str(item.get("serial") or "").strip(),
            tran=str(item.get("tran") or "").strip(),
            removable=bool(item.get("rm")),
            readonly=bool(item.get("ro")),
            mountpoints=str(item.get("mountpoints") or "").strip(),
        ))
    return devices


def size_bytes(path):
    return int(run(["blockdev", "--getsize64", path]).stdout.strip())
