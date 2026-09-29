import json
from unittest.mock import AsyncMock

import httpx
import pytest

from seeding.application.seeding_generation import generate_seeding_opportunities
from seeding.domain.errors.seeding_errors import OpportunityParseError, UngroundedSeedingError
from seeding.domain.models.seeding_result import SeedingResult
from seeding.domain.ports.model_router_port import ModelResult
from seeding.infrastructure.config.settings import SeedingSettings


async def test_generate_seeding_opportunities_round_trip_with_real_id():
    candidate = {
        "id": "cand-rt-001",
        "batch_id": "batch-rt",
        "document_id": "doc-rt",
        "claim_text": "A method for real-time data processing.",
        "problem": "Latency in existing systems.",
        "mechanism": "Parallel streaming architecture.",
        "tech_field": "Distributed Systems",
        "ipc_cpc_guess": "G06F 9/50",
    }
    candidates = [candidate]
    roadmap_context = None

    llm_response = {
        "opportunities": [
            {
                "category": "Whitespace",
                "title": "Enhanced Real-Time Processing",
                "description": "Novel approach to minimize latency further.",
                "innovation_rationale": (
                    "Builds on the parallel streaming but adds predictive caching."
                ),
                "roadmap_integration": "",
                "confidence": 0.8,
                "source_candidate_ids": ["cand-rt-001"],
            }
        ]
    }

    client = AsyncMock()
    client.complete = AsyncMock(
        return_value=ModelResult(content=json.dumps(llm_response), citations=[])
    )

    settings = SeedingSettings(model_router_url="http://test-router")

    result = await generate_seeding_opportunities(
        candidates, roadmap_context, client, settings, ai_model="gpt-test"
    )

    assert isinstance(result, SeedingResult)
    assert result.batch_id == "batch-rt"
    assert result.document_id == "doc-rt"
    assert result.engine == "seeding"
    assert len(result.opportunities) == 1
    opp = result.opportunities[0]
    assert opp.title == "Enhanced Real-Time Processing"
    assert "cand-rt-001" in opp.source_candidate_ids

    call_args = client.complete.await_args
    prompt_arg = call_args[0][0]
    assert "[CAND cand-rt-001]" in prompt_arg
    assert "real-time data processing" in prompt_arg.lower()


async def test_chunk_count_89_candidates_15_per_call():
    candidates = [
        {
            "id": f"cand-{i:03d}",
            "batch_id": "batch-chunk",
            "document_id": "doc-chunk",
            "claim_text": f"Claim {i}",
            "problem": f"Problem {i}",
            "mechanism": f"Mechanism {i}",
            "tech_field": "Field",
            "ipc_cpc_guess": "G06F",
        }
        for i in range(89)
    ]

    call_count = 0
    responses = []

    async def mock_complete(prompt: str, chunk_ids: list[str], model: str | None = None):
        nonlocal call_count
        call_count += 1
        opp = {
            "category": "Whitespace",
            "title": f"Opportunity {call_count}",
            "description": "Description",
            "innovation_rationale": "Rationale",
            "roadmap_integration": "",
            "confidence": 0.7,
            "source_candidate_ids": [chunk_ids[0]],
        }
        responses.append(chunk_ids)
        return ModelResult(content=json.dumps({"opportunities": [opp]}), citations=[])

    client = AsyncMock()
    client.complete = AsyncMock(side_effect=mock_complete)

    settings = SeedingSettings(
        model_router_url="http://test-router", seeding_candidates_per_call=15
    )

    result = await generate_seeding_opportunities(
        candidates, None, client, settings, ai_model="gpt-test"
    )

    assert call_count == 6
    assert len(responses) == 6
    assert len(responses[0]) == 15
    assert len(responses[1]) == 15
    assert len(responses[2]) == 15
    assert len(responses[3]) == 15
    assert len(responses[4]) == 15
    assert len(responses[5]) == 14
    assert len(result.opportunities) == 6


async def test_per_chunk_grounding():
    candidates = [
        {
            "id": f"cand-{i:03d}",
            "batch_id": "batch-ground",
            "document_id": "doc-ground",
            "claim_text": f"Claim {i}",
            "problem": f"Problem {i}",
            "mechanism": f"Mechanism {i}",
            "tech_field": "Field",
            "ipc_cpc_guess": "G06F",
        }
        for i in range(30)
    ]

    captured_chunks = []

    async def mock_complete(prompt: str, chunk_ids: list[str], model: str | None = None):
        captured_chunks.append(chunk_ids)
        opp = {
            "category": "Whitespace",
            "title": f"Opportunity for {chunk_ids[0]}",
            "description": "Description",
            "innovation_rationale": "Rationale",
            "roadmap_integration": "",
            "confidence": 0.7,
            "source_candidate_ids": [chunk_ids[0]],
        }
        return ModelResult(content=json.dumps({"opportunities": [opp]}), citations=[])

    client = AsyncMock()
    client.complete = AsyncMock(side_effect=mock_complete)

    settings = SeedingSettings(
        model_router_url="http://test-router", seeding_candidates_per_call=10
    )

    result = await generate_seeding_opportunities(
        candidates, None, client, settings, ai_model="gpt-test"
    )

    assert len(captured_chunks) == 3
    assert captured_chunks[0] == [f"cand-{i:03d}" for i in range(10)]
    assert captured_chunks[1] == [f"cand-{i:03d}" for i in range(10, 20)]
    assert captured_chunks[2] == [f"cand-{i:03d}" for i in range(20, 30)]
    assert len(result.opportunities) == 3


async def test_merge_covers_all_chunks():
    candidates = [
        {
            "id": f"cand-{i:03d}",
            "batch_id": "batch-merge",
            "document_id": "doc-merge",
            "claim_text": f"Claim {i}",
            "problem": f"Problem {i}",
            "mechanism": f"Mechanism {i}",
            "tech_field": "Field",
            "ipc_cpc_guess": "G06F",
        }
        for i in range(25)
    ]

    async def mock_complete(prompt: str, chunk_ids: list[str], model: str | None = None):
        opps = [
            {
                "category": "Whitespace",
                "title": f"Opp from {cid}",
                "description": "Description",
                "innovation_rationale": "Rationale",
                "roadmap_integration": "",
                "confidence": 0.7,
                "source_candidate_ids": [cid],
            }
            for cid in chunk_ids[:2]
        ]
        return ModelResult(content=json.dumps({"opportunities": opps}), citations=[])

    client = AsyncMock()
    client.complete = AsyncMock(side_effect=mock_complete)

    settings = SeedingSettings(
        model_router_url="http://test-router", seeding_candidates_per_call=10
    )

    result = await generate_seeding_opportunities(
        candidates, None, client, settings, ai_model="gpt-test"
    )

    assert len(result.opportunities) == 6


async def test_dedupe_by_normalized_title():
    candidates = [
        {
            "id": f"cand-{i:03d}",
            "batch_id": "batch-dedupe",
            "document_id": "doc-dedupe",
            "claim_text": f"Claim {i}",
            "problem": f"Problem {i}",
            "mechanism": f"Mechanism {i}",
            "tech_field": "Field",
            "ipc_cpc_guess": "G06F",
        }
        for i in range(20)
    ]

    chunk_responses = [
        [
            {
                "category": "Whitespace",
                "title": "Smart Ledger",
                "description": "First smart ledger",
                "innovation_rationale": "Rationale 1",
                "roadmap_integration": "",
                "confidence": 0.7,
                "source_candidate_ids": ["cand-000"],
            }
        ],
        [
            {
                "category": "Defensive",
                "title": "  smart   ledger  ",
                "description": "Second smart ledger",
                "innovation_rationale": "Rationale 2",
                "roadmap_integration": "",
                "confidence": 0.8,
                "source_candidate_ids": ["cand-010"],
            }
        ],
    ]

    call_index = 0

    async def mock_complete(prompt: str, chunk_ids: list[str], model: str | None = None):
        nonlocal call_index
        response = {"opportunities": chunk_responses[call_index]}
        call_index += 1
        return ModelResult(content=json.dumps(response), citations=[])

    client = AsyncMock()
    client.complete = AsyncMock(side_effect=mock_complete)

    settings = SeedingSettings(
        model_router_url="http://test-router", seeding_candidates_per_call=10
    )

    result = await generate_seeding_opportunities(
        candidates, None, client, settings, ai_model="gpt-test"
    )

    assert len(result.opportunities) == 1
    assert result.opportunities[0].title == "Smart Ledger"
    assert result.opportunities[0].description == "First smart ledger"


async def test_dedupe_keeps_all_blank_titles():
    candidates = [
        {
            "id": f"cand-{i:03d}",
            "batch_id": "batch-blank",
            "document_id": "doc-blank",
            "claim_text": f"Claim {i}",
            "problem": f"Problem {i}",
            "mechanism": f"Mechanism {i}",
            "tech_field": "Field",
            "ipc_cpc_guess": "G06F",
        }
        for i in range(20)
    ]

    chunk_responses = [
        [
            {
                "category": "Whitespace",
                "title": "",
                "description": "First blank",
                "innovation_rationale": "Rationale 1",
                "roadmap_integration": "",
                "confidence": 0.7,
                "source_candidate_ids": ["cand-000"],
            }
        ],
        [
            {
                "category": "Defensive",
                "title": "",
                "description": "Second blank",
                "innovation_rationale": "Rationale 2",
                "roadmap_integration": "",
                "confidence": 0.8,
                "source_candidate_ids": ["cand-010"],
            }
        ],
    ]

    call_index = 0

    async def mock_complete(prompt: str, chunk_ids: list[str], model: str | None = None):
        nonlocal call_index
        response = {"opportunities": chunk_responses[call_index]}
        call_index += 1
        return ModelResult(content=json.dumps(response), citations=[])

    client = AsyncMock()
    client.complete = AsyncMock(side_effect=mock_complete)

    settings = SeedingSettings(
        model_router_url="http://test-router", seeding_candidates_per_call=10
    )

    result = await generate_seeding_opportunities(
        candidates, None, client, settings, ai_model="gpt-test"
    )

    assert len(result.opportunities) == 2
    assert result.opportunities[0].title == ""
    assert result.opportunities[1].title == ""
    assert result.opportunities[0].description == "First blank"
    assert result.opportunities[1].description == "Second blank"


async def test_empty_chunk_tolerance():
    candidates = [
        {
            "id": f"cand-{i:03d}",
            "batch_id": "batch-empty",
            "document_id": "doc-empty",
            "claim_text": f"Claim {i}",
            "problem": f"Problem {i}",
            "mechanism": f"Mechanism {i}",
            "tech_field": "Field",
            "ipc_cpc_guess": "G06F",
        }
        for i in range(30)
    ]

    call_index = 0
    chunk_responses = [
        {"opportunities": []},
        {
            "opportunities": [
                {
                    "category": "Whitespace",
                    "title": "Valid Opportunity",
                    "description": "Good chunk",
                    "innovation_rationale": "Rationale",
                    "roadmap_integration": "",
                    "confidence": 0.7,
                    "source_candidate_ids": ["cand-010"],
                }
            ]
        },
        {"opportunities": []},
    ]

    async def mock_complete(prompt: str, chunk_ids: list[str], model: str | None = None):
        nonlocal call_index
        response = chunk_responses[call_index]
        call_index += 1
        return ModelResult(content=json.dumps(response), citations=[])

    client = AsyncMock()
    client.complete = AsyncMock(side_effect=mock_complete)

    settings = SeedingSettings(
        model_router_url="http://test-router", seeding_candidates_per_call=10
    )

    result = await generate_seeding_opportunities(
        candidates, None, client, settings, ai_model="gpt-test"
    )

    assert len(result.opportunities) == 1
    assert result.opportunities[0].title == "Valid Opportunity"


async def test_failed_chunk_tolerance():
    candidates = [
        {
            "id": f"cand-{i:03d}",
            "batch_id": "batch-fail",
            "document_id": "doc-fail",
            "claim_text": f"Claim {i}",
            "problem": f"Problem {i}",
            "mechanism": f"Mechanism {i}",
            "tech_field": "Field",
            "ipc_cpc_guess": "G06F",
        }
        for i in range(20)
    ]

    call_index = 0
    chunk_responses = [
        {
            "opportunities": [
                {
                    "category": "Whitespace",
                    "title": "Good Opportunity",
                    "description": "First chunk OK",
                    "innovation_rationale": "Rationale",
                    "roadmap_integration": "",
                    "confidence": 0.7,
                    "source_candidate_ids": ["cand-000"],
                }
            ]
        },
        {
            "opportunities": [
                {
                    "category": "Whitespace",
                    "title": "Bad Opportunity",
                    "description": "Ungrounded",
                    "innovation_rationale": "Rationale",
                    "roadmap_integration": "",
                    "confidence": 0.7,
                    "source_candidate_ids": ["cand-999"],
                }
            ]
        },
    ]

    async def mock_complete(prompt: str, chunk_ids: list[str], model: str | None = None):
        nonlocal call_index
        response = chunk_responses[call_index]
        call_index += 1
        return ModelResult(content=json.dumps(response), citations=[])

    client = AsyncMock()
    client.complete = AsyncMock(side_effect=mock_complete)

    settings = SeedingSettings(
        model_router_url="http://test-router", seeding_candidates_per_call=10
    )

    result = await generate_seeding_opportunities(
        candidates, None, client, settings, ai_model="gpt-test"
    )

    assert len(result.opportunities) == 1
    assert result.opportunities[0].title == "Good Opportunity"


async def test_all_empty_chunks_error():
    candidates = [
        {
            "id": f"cand-{i:03d}",
            "batch_id": "batch-allempty",
            "document_id": "doc-allempty",
            "claim_text": f"Claim {i}",
            "problem": f"Problem {i}",
            "mechanism": f"Mechanism {i}",
            "tech_field": "Field",
            "ipc_cpc_guess": "G06F",
        }
        for i in range(20)
    ]

    async def mock_complete(prompt: str, chunk_ids: list[str], model: str | None = None):
        return ModelResult(content=json.dumps({"opportunities": []}), citations=[])

    client = AsyncMock()
    client.complete = AsyncMock(side_effect=mock_complete)

    settings = SeedingSettings(
        model_router_url="http://test-router", seeding_candidates_per_call=10
    )

    with pytest.raises(OpportunityParseError, match="no opportunities from any chunk"):
        await generate_seeding_opportunities(
            candidates, None, client, settings, ai_model="gpt-test"
        )


async def test_transient_error_propagates():
    candidates = [
        {
            "id": "cand-001",
            "batch_id": "batch-transient",
            "document_id": "doc-transient",
            "claim_text": "Claim",
            "problem": "Problem",
            "mechanism": "Mechanism",
            "tech_field": "Field",
            "ipc_cpc_guess": "G06F",
        }
    ]

    async def mock_complete(prompt: str, chunk_ids: list[str], model: str | None = None):
        raise httpx.ReadTimeout("Request timeout")

    client = AsyncMock()
    client.complete = AsyncMock(side_effect=mock_complete)

    settings = SeedingSettings(model_router_url="http://test-router")

    with pytest.raises(httpx.ReadTimeout):
        await generate_seeding_opportunities(
            candidates, None, client, settings, ai_model="gpt-test"
        )


async def test_chunk_with_mixed_valid_invalid_keeps_valid():
    candidates = [
        {
            "id": f"cand-{i:03d}",
            "batch_id": "batch-mixed",
            "document_id": "doc-mixed",
            "claim_text": f"Claim {i}",
            "problem": f"Problem {i}",
            "mechanism": f"Mechanism {i}",
            "tech_field": "Field",
            "ipc_cpc_guess": "G06F",
        }
        for i in range(20)
    ]

    async def mock_complete(prompt: str, chunk_ids: list[str], model: str | None = None):
        opps = [
            {
                "category": "Whitespace",
                "title": "Valid Opportunity 1",
                "description": "First valid",
                "innovation_rationale": "Rationale",
                "roadmap_integration": "",
                "confidence": 0.7,
                "source_candidate_ids": [chunk_ids[0]],
            },
            {
                "category": "UnknownCategory",
                "title": "Invalid Opportunity",
                "description": "This will be dropped",
                "innovation_rationale": "Rationale",
                "roadmap_integration": "",
                "confidence": 0.7,
                "source_candidate_ids": [chunk_ids[1]],
            },
            {
                "category": "Defensive",
                "title": "Valid Opportunity 2",
                "description": "Second valid",
                "innovation_rationale": "Rationale",
                "roadmap_integration": "",
                "confidence": 0.8,
                "source_candidate_ids": [chunk_ids[2]],
            },
        ]
        return ModelResult(content=json.dumps({"opportunities": opps}), citations=[])

    client = AsyncMock()
    client.complete = AsyncMock(side_effect=mock_complete)

    settings = SeedingSettings(
        model_router_url="http://test-router", seeding_candidates_per_call=20
    )

    result = await generate_seeding_opportunities(
        candidates, None, client, settings, ai_model="gpt-test"
    )

    assert len(result.opportunities) == 2
    assert result.opportunities[0].title == "Valid Opportunity 1"
    assert result.opportunities[1].title == "Valid Opportunity 2"


async def test_all_invalid_chunk_contributes_empty_but_batch_succeeds():
    candidates = [
        {
            "id": f"cand-{i:03d}",
            "batch_id": "batch-allinvalid",
            "document_id": "doc-allinvalid",
            "claim_text": f"Claim {i}",
            "problem": f"Problem {i}",
            "mechanism": f"Mechanism {i}",
            "tech_field": "Field",
            "ipc_cpc_guess": "G06F",
        }
        for i in range(20)
    ]

    call_index = 0

    async def mock_complete(prompt: str, chunk_ids: list[str], model: str | None = None):
        nonlocal call_index
        call_index += 1
        if call_index == 1:
            return ModelResult(
                content=json.dumps(
                    {
                        "opportunities": [
                            {
                                "category": "UnknownCategory",
                                "title": "All invalid",
                                "description": "Will be dropped",
                                "innovation_rationale": "Rationale",
                                "roadmap_integration": "",
                                "confidence": 0.7,
                                "source_candidate_ids": ["cand-000"],
                            }
                        ]
                    }
                ),
                citations=[],
            )
        return ModelResult(
            content=json.dumps(
                {
                    "opportunities": [
                        {
                            "category": "Whitespace",
                            "title": "Valid from chunk 2",
                            "description": "Good",
                            "innovation_rationale": "Rationale",
                            "roadmap_integration": "",
                            "confidence": 0.7,
                            "source_candidate_ids": ["cand-010"],
                        }
                    ]
                }
            ),
            citations=[],
        )

    client = AsyncMock()
    client.complete = AsyncMock(side_effect=mock_complete)

    settings = SeedingSettings(
        model_router_url="http://test-router", seeding_candidates_per_call=10
    )

    result = await generate_seeding_opportunities(
        candidates, None, client, settings, ai_model="gpt-test"
    )

    assert len(result.opportunities) == 1
    assert result.opportunities[0].title == "Valid from chunk 2"


async def test_no_candidates_error():
    client = AsyncMock()
    settings = SeedingSettings(model_router_url="http://test-router")

    with pytest.raises(UngroundedSeedingError, match="no candidates available"):
        await generate_seeding_opportunities([], None, client, settings, ai_model="gpt-test")
