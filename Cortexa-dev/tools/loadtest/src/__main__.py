import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))

from harness import run_harness

if __name__ == "__main__":
    dry_run = "--dry-run" in sys.argv
    run_harness(dry_run=dry_run)
