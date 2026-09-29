import math

from extraction.infrastructure.config.settings import ExtractionSettings

_WORST_CASE_PER_CALL_LATENCY_SECONDS = 25.0
_RETRY_ATTEMPTS = 2
_TARGET_CEILING_FRACTION = 0.20


def _worst_case_unit_wall_clock_seconds(chunks_per_unit: int, concurrency: int) -> float:
    rounds = math.ceil(chunks_per_unit / concurrency)
    return rounds * _WORST_CASE_PER_CALL_LATENCY_SECONDS * _RETRY_ATTEMPTS


def test_max_chunks_per_unit_default_matches_capacity_doc():
    settings = ExtractionSettings(model_router_url="http://model-router-test")

    assert settings.max_chunks_per_unit == 25


def test_extraction_concurrency_default_matches_capacity_doc():
    settings = ExtractionSettings(model_router_url="http://model-router-test")

    assert settings.extraction_concurrency == 4


def test_session_lock_renewal_seconds_default_matches_capacity_doc():
    settings = ExtractionSettings(model_router_url="http://model-router-test")

    assert settings.session_lock_renewal_seconds == 1800.0


def test_worst_case_unit_wall_clock_stays_under_ceiling():
    settings = ExtractionSettings(model_router_url="http://model-router-test")

    wall_clock = _worst_case_unit_wall_clock_seconds(
        settings.max_chunks_per_unit, settings.extraction_concurrency
    )

    assert wall_clock == 350.0
    assert wall_clock < settings.session_lock_renewal_seconds * _TARGET_CEILING_FRACTION


def test_worst_case_unit_wall_clock_has_headroom_over_5x():
    settings = ExtractionSettings(model_router_url="http://model-router-test")

    wall_clock = _worst_case_unit_wall_clock_seconds(
        settings.max_chunks_per_unit, settings.extraction_concurrency
    )
    headroom_multiple = settings.session_lock_renewal_seconds / wall_clock

    assert headroom_multiple > 5.0


def test_nanogpt_chunk_count_requires_20_units_at_default_cap():
    settings = ExtractionSettings(model_router_url="http://model-router-test")
    nanogpt_chunk_count = 485

    unit_count = math.ceil(nanogpt_chunk_count / settings.max_chunks_per_unit)

    assert unit_count == 20
