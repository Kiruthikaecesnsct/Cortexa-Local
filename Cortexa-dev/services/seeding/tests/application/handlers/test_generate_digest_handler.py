import json
from unittest.mock import AsyncMock, patch

from seeding.application.handlers import generate_digest_handler
from seeding.application.handlers.generate_digest_handler import (
    GenerateDigestDeps,
    GenerateDigestHandler,
)
from seeding.application.handlers.process_seeding_request_handler import ProcessOutcome
from seeding.domain.errors.seeding_errors import ModelRouterFailedError
from seeding.domain.events.digest import DigestEnvelope, DigestPayload
from seeding.domain.models.digest import (
    CitedEntry,
    DigestReduceIntermediate,
    InventionContextBrief,
    SectionNote,
)
from seeding.domain.ports.model_router_port import ModelResult
from seeding.domain.services.chunk_grouping import section_key
from seeding.infrastructure.config.settings import SeedingSettings

BATCH_ID = "batch-1"
DOCUMENT_ID = "doc-1"
CORRELATION_ID = "corr-1"


def _settings(**overrides) -> SeedingSettings:
    base = {"model_router_url": "http://model-router.internal.test"}
    base.update(overrides)
    return SeedingSettings(**base)


def _envelope() -> DigestEnvelope:
    return DigestEnvelope(
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        correlation_id=CORRELATION_ID,
        payload=DigestPayload(document_id=DOCUMENT_ID, ai_model="gpt-x"),
    )


def _chunk(order_index: int, section: str) -> dict:
    return {
        "id": f"chunk-{order_index}",
        "order_index": order_index,
        "token_count": 10,
        "section_hint": section,
    }


def _map_json(chunk_id: str) -> str:
    return json.dumps({"claims_made": [{"text": "cuts memory", "chunk_ids": [chunk_id]}]})


def _reduce_json(chunk_ids: list[str]) -> str:
    return json.dumps(
        {
            "problem_space": {"text": "reduce memory cost", "chunk_ids": chunk_ids},
            "contributions": [{"text": "scheme", "chunk_ids": chunk_ids}],
        }
    )


def _result(content: str) -> ModelResult:
    return ModelResult(content=content, citations=[])


def _handler(
    *, chunks=None, brief=None, notes=None, complete=None, settings=None, intermediates=None
):
    chunk_repo = AsyncMock()
    chunk_repo.get_range = AsyncMock(return_value=chunks if chunks is not None else [])
    digest_repo = AsyncMock()
    digest_repo.get_brief = AsyncMock(return_value=brief)
    digest_repo.get_section_notes = AsyncMock(return_value=notes or [])
    digest_repo.get_intermediates = AsyncMock(return_value=intermediates or [])
    digest_repo.save_section_note = AsyncMock()
    digest_repo.save_intermediate = AsyncMock()
    digest_repo.save_brief = AsyncMock()
    client = AsyncMock()
    client.complete = complete or AsyncMock(return_value=_result(_reduce_json(["chunk-0"])))
    publisher = AsyncMock()
    deps = GenerateDigestDeps(
        chunk_repo=chunk_repo,
        digest_repo=digest_repo,
        client=client,
        publisher=publisher,
        settings=settings or _settings(),
    )
    return GenerateDigestHandler(deps), digest_repo, client, publisher


async def test_happy_path_reduces_saves_and_publishes_completed():
    complete = AsyncMock(
        side_effect=[_result(_map_json("chunk-0")), _result(_reduce_json(["chunk-0"]))]
    )
    handler, digest_repo, _, publisher = _handler(chunks=[_chunk(0, "Intro")], complete=complete)

    outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    digest_repo.save_brief.assert_awaited_once()
    event = publisher.publish.await_args.args[1]
    assert event["event_type"] == "digest.completed"
    assert event["payload"]["empty"] is False
    assert event["payload"]["section_count"] == 1
    assert publisher.publish.await_args.kwargs["session_id"] == BATCH_ID


async def test_zero_chunks_publishes_empty_brief_and_succeeds():
    handler, digest_repo, client, publisher = _handler(chunks=[])

    outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    client.complete.assert_not_awaited()
    saved = digest_repo.save_brief.await_args.args[0]
    assert saved.is_empty is True
    assert publisher.publish.await_args.args[1]["payload"]["empty"] is True


async def test_all_empty_notes_yield_empty_brief():
    complete = AsyncMock(return_value=_result(json.dumps({})))
    handler, digest_repo, _, publisher = _handler(chunks=[_chunk(0, "Refs")], complete=complete)

    outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    assert digest_repo.save_brief.await_args.args[0].is_empty is True
    assert publisher.publish.await_args.args[1]["payload"]["empty"] is True


async def test_existing_brief_republishes_without_regenerating():
    brief = InventionContextBrief(
        id="brief-1",
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        schema_version="1.0",
        prompt_version="1.0.0",
    )
    handler, digest_repo, client, publisher = _handler(chunks=[_chunk(0, "Intro")], brief=brief)

    outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    client.complete.assert_not_awaited()
    digest_repo.save_brief.assert_not_awaited()
    assert publisher.publish.await_args.args[1]["payload"]["brief_id"] == "brief-1"


async def test_resume_skips_already_saved_section_notes():
    existing_key = section_key(BATCH_ID, DOCUMENT_ID, "A#part0")
    existing = SectionNote(
        id=existing_key,
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        section_key=existing_key,
        section_label="A",
        chunk_ids=["chunk-0"],
        claims_made=[CitedEntry(text="prior", chunk_ids=["chunk-0"])],
        schema_version="1.0",
        prompt_version="1.0.0",
    )
    complete = AsyncMock(
        side_effect=[_result(_map_json("chunk-1")), _result(_reduce_json(["chunk-0", "chunk-1"]))]
    )
    handler, digest_repo, _, _ = _handler(
        chunks=[_chunk(0, "A"), _chunk(1, "B")], notes=[existing], complete=complete
    )

    outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    digest_repo.save_section_note.assert_awaited_once()
    saved = digest_repo.save_section_note.await_args.args[0]
    assert saved.section_label == "B"


async def test_section_notes_queried_with_current_prompt_version():
    handler, digest_repo, _, _ = _handler(
        chunks=[_chunk(0, "Intro")],
        complete=AsyncMock(
            side_effect=[_result(_map_json("chunk-0")), _result(_reduce_json(["chunk-0"]))]
        ),
    )

    await handler.handle(_envelope())

    digest_repo.get_section_notes.assert_awaited_once_with(BATCH_ID, DOCUMENT_ID, "1.0.0")


async def test_time_budget_exceeded_returns_transient():
    handler, digest_repo, _, publisher = _handler(
        chunks=[_chunk(0, "Intro")],
        settings=_settings(digest_message_time_budget_seconds=-1.0),
    )

    outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.TRANSIENT
    digest_repo.save_brief.assert_not_awaited()
    publisher.publish.assert_not_awaited()


async def test_model_router_503_is_transient():
    complete = AsyncMock(side_effect=ModelRouterFailedError("down", status_code=503))
    handler, _, _, publisher = _handler(chunks=[_chunk(0, "Intro")], complete=complete)

    outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.TRANSIENT
    publisher.publish.assert_not_awaited()


async def test_model_router_400_is_permanent_and_publishes_failed():
    complete = AsyncMock(side_effect=ModelRouterFailedError("bad", status_code=400))
    handler, _, _, publisher = _handler(chunks=[_chunk(0, "Intro")], complete=complete)

    outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.PERMANENT
    event = publisher.publish.await_args.args[1]
    assert event["event_type"] == "seeding.failed"
    assert publisher.publish.await_args.args[0] == handler._deps.settings.seeding_failed_topic


async def test_parse_error_is_permanent():
    complete = AsyncMock(return_value=_result("not json"))
    handler, _, _, publisher = _handler(chunks=[_chunk(0, "Intro")], complete=complete)

    outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.PERMANENT
    assert publisher.publish.await_args.args[1]["event_type"] == "seeding.failed"


def _hierarchical_settings(**overrides) -> SeedingSettings:
    base = {
        "digest_reduce_max_input_tokens": 1,
        "digest_reduce_max_depth": 1,
        "digest_reduce_max_fan": 8,
    }
    base.update(overrides)
    return _settings(**base)


def _intermediate(reduce_key: str, text: str, chunk_id: str) -> DigestReduceIntermediate:
    return DigestReduceIntermediate(
        id=f"int-{reduce_key}",
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        reduce_key=reduce_key,
        problem_space=CitedEntry(text=text, chunk_ids=[chunk_id]),
        schema_version="1.0",
        prompt_version="1.0.0",
    )


def _saved_note(section: str, order_index: int, chunk_id: str) -> SectionNote:
    key = section_key(BATCH_ID, DOCUMENT_ID, f"{section}#part0")
    return SectionNote(
        id=key,
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        section_key=key,
        section_label=section,
        chunk_ids=[chunk_id],
        claims_made=[CitedEntry(text=f"note-{section}", chunk_ids=[chunk_id])],
        schema_version="1.0",
        prompt_version="1.0.0",
    )


async def test_reduce_deadline_exceeded_persists_progress_then_transient():
    complete = AsyncMock(
        side_effect=[
            _result(_map_json("chunk-0")),
            _result(_map_json("chunk-1")),
            _result(_reduce_json(["chunk-0"])),
        ]
    )
    handler, digest_repo, _, publisher = _handler(
        chunks=[_chunk(0, "A"), _chunk(1, "B")],
        complete=complete,
        settings=_hierarchical_settings(digest_message_time_budget_seconds=100.0),
    )
    clock = [0.0, 1.0, 2.0, 50.0, 200.0]

    with patch.object(generate_digest_handler.time, "monotonic", side_effect=clock):
        outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.TRANSIENT
    digest_repo.save_intermediate.assert_awaited_once()
    digest_repo.save_brief.assert_not_awaited()
    publisher.publish.assert_not_awaited()


async def test_resume_reuses_persisted_intermediate_and_completes():
    cached = _intermediate("reduce:d0#part0", "CACHED-MERGE", "chunk-0")
    complete = AsyncMock(
        side_effect=[_result(_reduce_json(["chunk-1"])), _result(_reduce_json(["chunk-0"]))]
    )
    handler, digest_repo, client, publisher = _handler(
        chunks=[_chunk(0, "A"), _chunk(1, "B")],
        notes=[_saved_note("A", 0, "chunk-0"), _saved_note("B", 1, "chunk-1")],
        intermediates=[cached],
        complete=complete,
        settings=_hierarchical_settings(),
    )

    outcome = await handler.handle(_envelope())

    assert outcome == ProcessOutcome.SUCCESS
    assert client.complete.await_count == 2
    digest_repo.save_intermediate.assert_awaited_once()
    digest_repo.save_brief.assert_awaited_once()
    final_prompt = client.complete.await_args_list[-1].args[0]
    assert "CACHED-MERGE" in final_prompt
    assert publisher.publish.await_args.args[1]["payload"]["empty"] is False


async def test_resume_final_brief_matches_single_pass_output():
    reduce_content = _reduce_json(["chunk-0", "chunk-1"])
    cached = _intermediate("reduce:d0#part0", "CACHED-MERGE", "chunk-0")
    notes = [_saved_note("A", 0, "chunk-0"), _saved_note("B", 1, "chunk-1")]

    resumed_complete = AsyncMock(
        side_effect=[_result(_reduce_json(["chunk-1"])), _result(reduce_content)]
    )
    resumed, resumed_repo, _, _ = _handler(
        chunks=[_chunk(0, "A"), _chunk(1, "B")],
        notes=notes,
        intermediates=[cached],
        complete=resumed_complete,
        settings=_hierarchical_settings(),
    )
    await resumed.handle(_envelope())
    resumed_brief = resumed_repo.save_brief.await_args.args[0]

    assert resumed_brief.problem_space.text == "reduce memory cost"
    assert resumed_brief.contributions[0].chunk_ids == ["chunk-0", "chunk-1"]
    assert resumed_brief.is_empty is False
