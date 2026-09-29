import asyncio
import logging
from typing import Literal


class ConsumerHealth:
    def __init__(self) -> None:
        self._state: Literal["starting", "alive", "dead"] = "starting"
        self._exception: BaseException | None = None

    def mark_alive(self) -> None:
        self._state = "alive"

    def mark_dead(self, exc: BaseException | None) -> None:
        self._state = "dead"
        self._exception = exc

    @property
    def is_serving(self) -> bool:
        return self._state in ("starting", "alive")


def configure_logging() -> None:
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s %(levelname)s %(name)s %(message)s",
    )
    logging.getLogger("azure").setLevel(logging.WARNING)


def supervise(
    task: asyncio.Task,
    health: ConsumerHealth,
    logger: logging.Logger,
) -> None:
    def _on_done(t: asyncio.Task) -> None:
        if t.cancelled():
            return

        exc = t.exception()
        if exc is not None:
            logger.critical("consumer task died", exc_info=exc)
        else:
            logger.critical("consumer task exited unexpectedly")

        health.mark_dead(exc)

    task.add_done_callback(_on_done)
