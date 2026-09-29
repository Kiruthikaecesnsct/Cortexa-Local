import logging
import shutil
from pathlib import Path

logger = logging.getLogger(__name__)


def remove_clone(path: str) -> None:
    try:
        shutil.rmtree(path, ignore_errors=True)
        logger.info("Removed clone directory: %s", path)
    except Exception as exc:
        logger.warning("Failed to remove clone directory %s: %s", path, exc)


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
                shutil.rmtree(item, ignore_errors=True)
                removed_count += 1
                logger.info("Swept orphaned clone: %s", item)
            except Exception as exc:
                logger.warning("Failed to sweep orphaned clone %s: %s", item, exc)

    logger.info("Swept %d orphaned clone(s) from %s", removed_count, clone_workdir)
