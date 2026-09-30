import sentry_sdk
from sentry_sdk import logger as sentry_logger
from sentry_sdk import metrics
from sentry_sdk.scrubber import DEFAULT_DENYLIST, EventScrubber

from ingestion.infrastructure.config.settings import IngestionSettings

# GitHub scan requests carry the user's personal access token in the "pat" body field.
_EXTRA_DENYLIST = ["pat"]


def configure_sentry(settings: IngestionSettings) -> None:
    if not settings.sentry_dsn:
        return
    sentry_sdk.init(
        dsn=settings.sentry_dsn,
        environment=settings.sentry_environment,
        traces_sample_rate=settings.sentry_traces_sample_rate,
        send_default_pii=False,
        event_scrubber=EventScrubber(denylist=DEFAULT_DENYLIST + _EXTRA_DENYLIST),
        enable_logs=True,
    )
    # Metrics are auto-enabled once the SDK is initialised; emit a startup counter.
    metrics.count("service.started", 1)
    sentry_logger.info("ingestion service started")
