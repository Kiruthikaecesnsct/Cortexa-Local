import json
import math
import time
from unittest.mock import AsyncMock, patch

from seeding.application.ideation.round_engine import (
    IdeationContext,
    RoundEngine,
    RoundEngineDeps,
    RoundStatus,
)
from seeding.domain.enums.opportunity_category import OpportunityCategory
from seeding.domain.models.digest import CitedEntry, InventionContextBrief
from seeding.domain.models.ideation import RoundRecord
from seeding.domain.models.scratchpad import AcceptedIdea, IdeationScratchpad
from seeding.domain.ports.model_router_port import ModelResult
from seeding.infrastructure.config.settings import SeedingSettings
from seeding.infrastructure.cosmos.scratchpad_repository import scratchpad_id

BATCH_ID = "batch-1"
DOCUMENT_ID = "doc-1"
KNOWN_CHUNK_IDS = ["chunk-1", "chunk-2"]

_VECTOR_HITS = [
    {"payload": {"chunk_id": "chunk-1", "section_label": "sec-1", "text_excerpt": "excerpt one"}},
    {"payload": {"chunk_id": "chunk-2", "section_label": "sec-2", "text_excerpt": "excerpt two"}},
]


class ScriptedModelRouter:
    def __init__(self, responses: list[ModelResult]) -> None:
        self._responses = list(responses)
        self.calls: list[str] = []

    async def complete(self, prompt: str, evidence_refs: list[str], model: str | None = None):
        self.calls.append(prompt)
        return self._responses.pop(0)


def _settings(**overrides) -> SeedingSettings:
    return SeedingSettings(model_router_url="http://model-router.internal.test", **overrides)


def _brief() -> InventionContextBrief:
    return InventionContextBrief(
        id="brief-1",
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        schema_version="1.0",
        prompt_version="1.0.0",
        problem_space=CitedEntry(text="adaptive scheduling for distributed jobs", chunk_ids=[]),
        contributions=[
            CitedEntry(text="adaptive step scheduling contribution", chunk_ids=["chunk-1"])
        ],
        future_work=[CitedEntry(text="explore multi-agent coordination", chunk_ids=["chunk-2"])],
    )


def _chunk_repo() -> AsyncMock:
    repo = AsyncMock()
    repo.get_range.return_value = [{"id": cid} for cid in KNOWN_CHUNK_IDS]
    return repo


def _digest_repo(brief: InventionContextBrief | None) -> AsyncMock:
    repo = AsyncMock()
    repo.get_brief.return_value = brief
    return repo


def _landscape_repo() -> AsyncMock:
    repo = AsyncMock()
    repo.get_landscape.return_value = None
    return repo


def _vector_client() -> AsyncMock:
    client = AsyncMock()

    async def _embed(texts):
        return [[0.1] for _ in texts]

    async def _search(vector, top_k, target, filters=None):
        return _VECTOR_HITS

    client.embed.side_effect = _embed
    client.search.side_effect = _search
    return client


def _scratchpad_repo(existing: IdeationScratchpad | None = None) -> AsyncMock:
    repo = AsyncMock()
    repo.get.return_value = existing
    return repo


def _ctx(deadline: float | None = None) -> IdeationContext:
    return IdeationContext(
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        correlation_id="corr-1",
        ai_model=None,
        roadmap_context=None,
        deadline=deadline if deadline is not None else time.monotonic() + 60,
    )


_UNSET = object()


def _engine(
    client: ScriptedModelRouter,
    settings: SeedingSettings,
    scratchpad_repo: AsyncMock | None = None,
    brief: InventionContextBrief | None = _UNSET,
) -> tuple[RoundEngine, AsyncMock]:
    scratch_repo = scratchpad_repo or _scratchpad_repo()
    resolved_brief = _brief() if brief is _UNSET else brief
    deps = RoundEngineDeps(
        chunk_repo=_chunk_repo(),
        digest_repo=_digest_repo(resolved_brief),
        landscape_repo=_landscape_repo(),
        scratchpad_repo=scratch_repo,
        vector_client=_vector_client(),
        client=client,
        settings=settings,
    )
    return RoundEngine(deps), scratch_repo


def _propose_content(*ideas: dict) -> str:
    return json.dumps({"ideas": list(ideas)})


def _critique_content(*verdicts: dict) -> str:
    return json.dumps({"verdicts": list(verdicts)})


def _refine_content(*opportunities: dict) -> str:
    return json.dumps({"opportunities": list(opportunities)})


def _sketch(sketch_id: str, chunk_id: str, novelty_delta: str, target_concept: str) -> dict:
    return {
        "sketch_id": sketch_id,
        "title": f"idea {sketch_id}",
        "summary": "summary text",
        "chunk_ids": [chunk_id],
        "novelty_delta": novelty_delta,
        "target_concept": target_concept,
    }


def _opportunity(sketch_id: str, chunk_id: str, novelty_delta: str) -> dict:
    return {
        "sketch_id": sketch_id,
        "title": f"opportunity {sketch_id}",
        "description": "full description of the opportunity",
        "mechanism": "technical mechanism detail",
        "claim_statement": "A method comprising a novel step.",
        "category": OpportunityCategory.Whitespace.value,
        "novelty_delta": novelty_delta,
        "chunk_ids": [chunk_id],
        "roadmap_alignment": "",
    }


async def test_critique_rejects_restated_idea_accepts_genuine_extension():
    settings = _settings(ideation_max_rounds=1)
    responses = [
        ModelResult(
            content=_propose_content(
                _sketch("s1", "chunk-1", "Same as document's adaptive scheduling", "scheduling"),
                _sketch(
                    "s2",
                    "chunk-2",
                    "Adds cross-agent negotiation protocol absent from the document",
                    "multi-agent coordination",
                ),
            ),
            citations=[],
        ),
        ModelResult(
            content=_critique_content(
                {
                    "sketch_id": "s1",
                    "verdict": "reject",
                    "reason_code": "restates_document",
                    "note": "restates contribution",
                },
                {
                    "sketch_id": "s2",
                    "verdict": "accept",
                    "reason_code": "accept",
                    "note": "genuine extension",
                },
            ),
            citations=[],
        ),
        ModelResult(
            content=_refine_content(
                _opportunity(
                    "s2",
                    "chunk-2",
                    "Adds cross-agent negotiation protocol absent from the document",
                )
            ),
            citations=[],
        ),
    ]
    client = ScriptedModelRouter(responses)
    engine, _ = _engine(client, settings)

    result = await engine.run(_ctx())

    assert result.status == RoundStatus.COMPLETE
    scratchpad = result.scratchpad
    assert len(scratchpad.accepted_ideas) == 1
    accepted = scratchpad.accepted_ideas[0]
    assert accepted.chunk_ids == ["chunk-2"]
    assert (
        accepted.novelty_delta == "Adds cross-agent negotiation protocol absent from the document"
    )
    assert len(scratchpad.rejections) == 1
    assert scratchpad.rejections[0].reason_code == "restates_document"


async def test_round_two_duplicate_of_round_one_accepted_idea_rejected():
    settings = _settings(ideation_max_rounds=3)
    round1 = [
        ModelResult(
            content=_propose_content(_sketch("s1", "chunk-1", "Novel delta one", "concept-a")),
            citations=[],
        ),
        ModelResult(
            content=_critique_content(
                {"sketch_id": "s1", "verdict": "accept", "reason_code": "accept", "note": "ok"}
            ),
            citations=[],
        ),
        ModelResult(
            content=_refine_content(_opportunity("s1", "chunk-1", "Novel delta one")),
            citations=[],
        ),
    ]
    round2 = [
        ModelResult(
            content=_propose_content(_sketch("s2", "chunk-2", "Novel delta one", "concept-a")),
            citations=[],
        ),
        ModelResult(
            content=_critique_content(
                {
                    "sketch_id": "s2",
                    "verdict": "reject",
                    "reason_code": "duplicate_accepted",
                    "note": "matches round-1 accepted idea",
                }
            ),
            citations=[],
        ),
    ]
    client = ScriptedModelRouter(round1 + round2)
    engine, _ = _engine(client, settings)

    result = await engine.run(_ctx())

    scratchpad = result.scratchpad
    assert scratchpad.rounds_completed == 2
    assert len(scratchpad.accepted_ideas) == 1
    assert len(scratchpad.rejections) == 1
    assert scratchpad.rejections[0].reason_code == "duplicate_accepted"


async def test_early_stop_when_round_accepts_zero_new_ideas():
    settings = _settings(ideation_max_rounds=5)
    responses = [
        ModelResult(
            content=_propose_content(_sketch("s1", "chunk-1", "delta", "concept-a")), citations=[]
        ),
        ModelResult(
            content=_critique_content(
                {
                    "sketch_id": "s1",
                    "verdict": "reject",
                    "reason_code": "restates_document",
                    "note": "restates",
                }
            ),
            citations=[],
        ),
    ]
    client = ScriptedModelRouter(responses)
    engine, _ = _engine(client, settings)

    result = await engine.run(_ctx())

    assert result.status == RoundStatus.EXHAUSTED
    assert result.scratchpad.rounds_completed == 1
    assert len(client.calls) == 2


async def test_max_rounds_stops_the_loop():
    settings = _settings(ideation_max_rounds=2)
    round1 = [
        ModelResult(
            content=_propose_content(_sketch("s1", "chunk-1", "delta one", "concept-a")),
            citations=[],
        ),
        ModelResult(
            content=_critique_content(
                {"sketch_id": "s1", "verdict": "accept", "reason_code": "accept", "note": "ok"}
            ),
            citations=[],
        ),
        ModelResult(
            content=_refine_content(_opportunity("s1", "chunk-1", "delta one")), citations=[]
        ),
    ]
    round2 = [
        ModelResult(
            content=_propose_content(_sketch("s2", "chunk-2", "delta two", "concept-b")),
            citations=[],
        ),
        ModelResult(
            content=_critique_content(
                {"sketch_id": "s2", "verdict": "accept", "reason_code": "accept", "note": "ok"}
            ),
            citations=[],
        ),
        ModelResult(
            content=_refine_content(_opportunity("s2", "chunk-2", "delta two")), citations=[]
        ),
    ]
    client = ScriptedModelRouter(round1 + round2)
    engine, _ = _engine(client, settings)

    result = await engine.run(_ctx())

    assert result.status == RoundStatus.COMPLETE
    assert result.scratchpad.rounds_completed == 2
    assert len(result.scratchpad.accepted_ideas) == 2
    assert len(client.calls) == 6


async def test_deadline_hit_mid_run_returns_resume_needed_with_progress_persisted():
    settings = _settings(ideation_max_rounds=3)
    round1 = [
        ModelResult(
            content=_propose_content(_sketch("s1", "chunk-1", "delta one", "concept-a")),
            citations=[],
        ),
        ModelResult(
            content=_critique_content(
                {"sketch_id": "s1", "verdict": "accept", "reason_code": "accept", "note": "ok"}
            ),
            citations=[],
        ),
        ModelResult(
            content=_refine_content(_opportunity("s1", "chunk-1", "delta one")), citations=[]
        ),
    ]
    client = ScriptedModelRouter(round1)
    engine, scratch_repo = _engine(client, settings)
    deadline = 100.0
    ctx = _ctx(deadline=deadline)
    monotonic_values = [0.0, 0.0, 0.0, 0.0] + [deadline] * 10

    with patch("time.monotonic", side_effect=monotonic_values):
        result = await engine.run(ctx)

    assert result.status == RoundStatus.RESUME_NEEDED
    assert result.scratchpad.rounds_completed == 1
    assert len(result.scratchpad.accepted_ideas) == 1
    assert scratch_repo.save.await_count >= 1


async def test_resume_from_persisted_scratchpad_continues_at_right_round():
    settings = _settings(ideation_max_rounds=2)
    existing = IdeationScratchpad(
        id=scratchpad_id(BATCH_ID, DOCUMENT_ID, settings.ideation_schema_version),
        batch_id=BATCH_ID,
        document_id=DOCUMENT_ID,
        schema_version=settings.ideation_schema_version,
        prompt_version=settings.ideation_prompt_version,
        rounds_completed=1,
        accepted_ideas=[AcceptedIdea(title="round0 idea", chunk_ids=["chunk-1"], round_index=0)],
        rounds=[RoundRecord(round=0, accepted_count=1)],
        status="in_progress",
    )
    responses = [
        ModelResult(
            content=_propose_content(_sketch("s-new", "chunk-2", "new delta", "concept-b")),
            citations=[],
        ),
        ModelResult(
            content=_critique_content(
                {"sketch_id": "s-new", "verdict": "accept", "reason_code": "accept", "note": "ok"}
            ),
            citations=[],
        ),
        ModelResult(
            content=_refine_content(_opportunity("s-new", "chunk-2", "new delta")), citations=[]
        ),
    ]
    client = ScriptedModelRouter(responses)
    scratch_repo = _scratchpad_repo(existing)
    engine, _ = _engine(client, settings, scratchpad_repo=scratch_repo)

    result = await engine.run(_ctx())

    assert result.status == RoundStatus.COMPLETE
    assert result.scratchpad.rounds_completed == 2
    assert len(result.scratchpad.accepted_ideas) == 2
    assert len(client.calls) == 3


def _big_vector_client(text_len: int) -> AsyncMock:
    client = AsyncMock()
    big = "x" * text_len
    hits = [
        {"payload": {"chunk_id": "chunk-1", "section_label": "sec-1", "text_excerpt": big}},
        {"payload": {"chunk_id": "chunk-2", "section_label": "sec-2", "text_excerpt": big}},
    ]

    async def _embed(texts):
        return [[0.1] for _ in texts]

    async def _search(vector, top_k, target, filters=None):
        return hits

    client.embed.side_effect = _embed
    client.search.side_effect = _search
    return client


async def test_over_budget_propose_prompt_trimmed_under_cap_and_call_proceeds():
    cap = 1500
    settings = _settings(
        ideation_max_rounds=1,
        ideation_excerpt_char_limit=4000,
        ideation_propose_max_input_tokens=cap,
    )
    responses = [
        ModelResult(
            content=_propose_content(
                _sketch(
                    "s1",
                    "chunk-2",
                    "Adds cross-agent negotiation protocol absent from the document",
                    "multi-agent coordination",
                )
            ),
            citations=[],
        ),
        ModelResult(
            content=_critique_content(
                {"sketch_id": "s1", "verdict": "accept", "reason_code": "accept", "note": "ok"}
            ),
            citations=[],
        ),
        ModelResult(
            content=_refine_content(
                _opportunity(
                    "s1",
                    "chunk-2",
                    "Adds cross-agent negotiation protocol absent from the document",
                )
            ),
            citations=[],
        ),
    ]
    client = ScriptedModelRouter(responses)
    deps = RoundEngineDeps(
        chunk_repo=_chunk_repo(),
        digest_repo=_digest_repo(_brief()),
        landscape_repo=_landscape_repo(),
        scratchpad_repo=_scratchpad_repo(),
        vector_client=_big_vector_client(4000),
        client=client,
        settings=settings,
    )
    engine = RoundEngine(deps)

    result = await engine.run(_ctx())

    assert result.status == RoundStatus.COMPLETE
    assert len(result.scratchpad.accepted_ideas) == 1
    assert math.ceil(len(client.calls[0]) / 4) <= cap


async def test_resume_needed_when_digest_brief_not_yet_available():
    settings = _settings()
    client = ScriptedModelRouter([])
    engine, scratch_repo = _engine(client, settings, brief=None)

    result = await engine.run(_ctx())

    assert result.status == RoundStatus.RESUME_NEEDED
    assert client.calls == []
