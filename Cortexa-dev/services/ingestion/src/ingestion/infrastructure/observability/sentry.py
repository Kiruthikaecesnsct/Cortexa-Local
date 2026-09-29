import sentry_sdk
from sentry_sdk import logger as sentry_logger
from sentry_sdk import metrics

from ingestion.infrastructure.config.settings import IngestionSettings


def configure_sentry(settings: IngestionSettings) -> None:
    if not settings.sentry_dsn:
        return
    sentry_sdk.init(
        dsn=settings.sentry_dsn,
        environment=settings.sentry_environment,
        traces_sample_rate=settings.sentry_traces_sample_rate,
        send_default_pii=False,
        enable_logs=True,
    )
    # Metrics are auto-enabled once the SDK is initialised; emit a startup counter.
    metrics.count("service.started", 1)
    sentry_logger.info("ingestion service started")
