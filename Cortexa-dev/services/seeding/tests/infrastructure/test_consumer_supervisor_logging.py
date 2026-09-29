import logging

from seeding.infrastructure.observability.consumer_supervisor import configure_logging


def test_configure_logging_silences_azure_but_keeps_app_logger_info():
    root = logging.getLogger()
    saved_handlers = root.handlers[:]
    saved_level = root.level
    root.handlers = []
    try:
        configure_logging()

        assert logging.getLogger("azure").getEffectiveLevel() == logging.WARNING
        assert logging.getLogger("seeding.main").getEffectiveLevel() == logging.INFO
    finally:
        root.handlers = saved_handlers
        root.setLevel(saved_level)
