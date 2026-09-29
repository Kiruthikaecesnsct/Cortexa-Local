import hashlib

from seeding.domain.models.scratchpad import AcceptedIdea

_ID_PREFIX = "seed"
_ENGINE = "seeding"


def _deterministic_id(batch_id: str, document_id: str, round_index: int, title: str) -> str:
    key = f"{batch_id}:{document_id}:{round_index}:{title}"
    digest = hashlib.sha256(key.encode("utf-8")).hexdigest()
    return f"{_ID_PREFIX}-{digest}"


def _provenance(idea: AcceptedIdea) -> dict:
    return {
        "chunk_ids": list(idea.chunk_ids),
        "excerpts": [excerpt.model_dump(mode="json") for excerpt in idea.excerpts],
    }


def map_accepted_idea(idea: AcceptedIdea, batch_id: str, document_id: str) -> dict:
    return {
        "id": _deterministic_id(batch_id, document_id, idea.round_index, idea.title),
        "batch_id": batch_id,
        "document_id": document_id,
        "engine": _ENGINE,
        "claim_text": idea.claim_statement,
        "problem": idea.summary or idea.target_concept,
        "target_concept": idea.target_concept,
        "mechanism": idea.mechanism,
        "provenance": _provenance(idea),
        "title": idea.title,
        "description": idea.description or idea.summary,
        "category": idea.category,
        "novelty_delta": idea.novelty_delta,
        "roadmap_alignment": idea.roadmap_alignment,
        "round_index": idea.round_index,
    }


def map_accepted_ideas(ideas: list[AcceptedIdea], batch_id: str, document_id: str) -> list[dict]:
    return [map_accepted_idea(idea, batch_id, document_id) for idea in ideas]
