import os
import time
from pathlib import Path


def parse_block_size(value):
    value = (value or "4M").strip().upper()
    mult = {"K": 1024, "M": 1024**2, "G": 1024**3}
    if value[-1:] in mult:
        return int(value[:-1]) * mult[value[-1]]
    return int(value)


def create_raw_image(source, destination: Path, block_size="4M", progress=None, should_stop=None):
    bs = parse_block_size(block_size)
    copied = 0
    start = time.time()

    with open(source, "rb", buffering=0) as src, destination.open("wb") as dst:
        while True:
            if should_stop and should_stop():
                raise RuntimeError("Imaging cancelled by user.")

            chunk = src.read(bs)
            if not chunk:
                break

            dst.write(chunk)
            copied += len(chunk)

            if progress:
                elapsed = max(time.time() - start, 0.001)
                progress(copied, copied / elapsed)

    os.sync()
    return copied
