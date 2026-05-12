import hashlib
from pathlib import Path

SUPPORTED_HASHES = ["md5", "sha1", "sha256"]


def hash_file(path: Path, algorithms, progress=None):
    hashers = {algo: hashlib.new(algo) for algo in algorithms}
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            for h in hashers.values():
                h.update(chunk)
            if progress:
                progress(len(chunk))
    return {name: h.hexdigest() for name, h in hashers.items()}


def write_hash_report(image_path: Path, hashes):
    report = image_path.with_suffix(image_path.suffix + ".hashes.txt")
    report.write_text("\n".join(
        f"{algo.upper()}: {digest}  {image_path.name}"
        for algo, digest in hashes.items()
    ) + "\n")
    return report


def read_hash_report(report: Path):
    values = {}
    for line in report.read_text().splitlines():
        if ":" in line:
            name, rest = line.split(":", 1)
            values[name.lower()] = rest.strip().split()[0]
    return values
