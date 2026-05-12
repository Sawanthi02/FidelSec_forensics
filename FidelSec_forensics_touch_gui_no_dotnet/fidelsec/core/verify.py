from pathlib import Path
from .hash_utils import hash_file, read_hash_report


def verify_image(image: Path, report: Path):
    expected = read_hash_report(report)
    actual = hash_file(image, list(expected.keys()))
    return {
        algo: {
            "expected": expected[algo],
            "actual": actual[algo],
            "ok": expected[algo].lower() == actual[algo].lower(),
        }
        for algo in expected
    }
