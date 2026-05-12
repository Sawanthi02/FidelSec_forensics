from datetime import datetime
from pathlib import Path
import platform


class CaseLogger:
    def __init__(self, path: Path):
        self.path = path
        self.path.parent.mkdir(parents=True, exist_ok=True)

    def start(self, case_name, examiner, source, output):
        self.path.write_text(
            "\n".join([
                "FidelSec Forensic Imager Log",
                f"Timestamp: {datetime.now().isoformat()}",
                f"Case: {case_name}",
                f"Examiner: {examiner}",
                f"Source: {source}",
                f"Output: {output}",
                f"Host: {platform.node()}",
                f"System: {platform.platform()}",
                ""
            ])
        )

    def append(self, message):
        with self.path.open("a") as f:
            f.write(f"[{datetime.now().isoformat()}] {message}\n")
