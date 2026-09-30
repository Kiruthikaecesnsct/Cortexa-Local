import logging
import os
import shutil
import stat
from pathlib import Path

logger = logging.getLogger(__name__)


def _clear_readonly_and_retry(func, path, _exc) -> None:
    # Git marks pack files read-only; Windows refuses to delete those until the flag is cleared.
    try:
        os.chmod(path, stat.S_IWRITE)
        func(path)
    except OSError as exc:
        logger.warning("Could not remove %s: %s", path, exc)


def remove_clone(path: str) -> None:
    shutil.rmtree(path, onexc=_clear_readonly_and_retry)
    logger.info("Removed clone directory: %s", path)


def sweep_workdir(clone_workdir: str) -> None:
    workdir = Path(clone_workdir)
    if not workdir.exists():
        # Create it so the first clone on a cold container does not fail on a
        # missing parent directory; there is nothing to sweep yet.
        workdir.mkdir(parents=True, exist_ok=True)
        logger.info("Created clone workdir: %s", clone_workdir)
        return

    removed_count = 0
    for item in workdir.iterdir():
        if item.is_dir():
            try:
                shutil.rmtree(item, onexc=_clear_readonly_and_retry)
                removed_count += 1
                logger.info("Swept orphaned clone: %s", item)
            except Exception as exc:
                logger.warning("Failed to sweep orphaned clone %s: %s", item, exc)

    logger.info("Swept %d orphaned clone(s) from %s", removed_count, clone_workdir)
