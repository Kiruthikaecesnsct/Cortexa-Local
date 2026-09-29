"""Curated discrimination fixtures for extraction candidate selection.

These hand-written chunks are the shared contract for two later test surfaces:

1. Parser tests — the ``representative_output`` of each fixture is a valid
   ``{"candidates": [...]}`` payload that :class:`CandidateParser` must accept.
2. A gated live eval — each chunk is fed to the real model and the returned
   candidate count is asserted to fall within ``[expected_min, expected_max]``.

The point of the set is source discrimination, NOT novelty judgment. A
correctly-discriminating model keeps the mechanisms a chunk presents as its own
contribution and drops mechanisms that are merely cited as prior art/background
or applied off-the-shelf from an external source. Novelty and patentability are
decided downstream by the evidence and scoring stages, never at extraction.

``chunk_type`` is one of:

- ``"pure_background"``      — only cited prior art / background; expect 0.
- ``"clear_contribution"``  — the text's own method/design; expect >= 1.
- ``"applied_off_the_shelf"`` — standard components used as-is; expect 0.
- ``"mixed"``               — contribution plus cited/applied parts; expect the
  contribution part(s) only.

Each ``representative_output`` conforms to the extraction candidate schema
(``claim_text``, ``problem``, ``mechanism``, ``tech_field``, ``ipc_cpc_guess``,
optional ``source_span``). For empty-expectation chunks the payload is
``{"candidates": []}``.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Literal

ChunkType = Literal[
    "pure_background",
    "clear_contribution",
    "applied_off_the_shelf",
    "mixed",
]


@dataclass(frozen=True)
class DiscriminationChunk:
    """A hand-labelled chunk plus the discrimination contract it must satisfy."""

    chunk_id: str
    chunk_type: ChunkType
    text: str
    representative_output: dict[str, Any]
    expected_min: int
    expected_max: int
    notes: str = field(default="")

    def __post_init__(self) -> None:
        if self.expected_min < 0 or self.expected_max < self.expected_min:
            raise ValueError(f"{self.chunk_id}: invalid expected band")
        candidates = self.representative_output.get("candidates")
        if not isinstance(candidates, list):
            raise ValueError(f"{self.chunk_id}: representative_output needs a candidates list")
        if not (self.expected_min <= len(candidates) <= self.expected_max):
            raise ValueError(
                f"{self.chunk_id}: representative_output has {len(candidates)} candidates, "
                f"outside band [{self.expected_min}, {self.expected_max}]"
            )


def _empty() -> dict[str, Any]:
    return {"candidates": []}


DISCRIMINATION_CHUNKS: list[DiscriminationChunk] = [
    # ---------------------------------------------------------------- background
    DiscriminationChunk(
        chunk_id="bg-transformers-recap",
        chunk_type="pure_background",
        text=(
            "The Transformer architecture of Vaswani et al. [12] replaced recurrence with "
            "self-attention and became the standard backbone for sequence modelling. Prior "
            "approaches such as the LSTM [7] and the GRU [3] processed tokens sequentially, "
            "which limited parallelism. We build on this well-established line of work."
        ),
        representative_output=_empty(),
        expected_min=0,
        expected_max=0,
        notes="Every mechanism named is attributed to prior work via citation markers.",
    ),
    DiscriminationChunk(
        chunk_id="bg-related-work-survey",
        chunk_type="pure_background",
        text=(
            "Related work. Existing patent-retrieval systems fall into three families. "
            "Keyword systems [1,2] match claim terms directly. Classification-based systems [4] "
            "route queries through IPC codes. Dense-retrieval systems [8,9] embed claims with a "
            "pretrained encoder. None of these is modified in this paper; we survey them for "
            "context only."
        ),
        representative_output=_empty(),
        expected_min=0,
        expected_max=0,
        notes="Explicitly a survey; nothing is proposed here.",
    ),
    DiscriminationChunk(
        chunk_id="bg-citation-list",
        chunk_type="pure_background",
        text=(
            "References\n"
            "[1] A. Smith, Sparse Attention for Long Documents, 2021.\n"
            "[2] B. Lee, Patent Landscaping at Scale, 2020.\n"
            "[3] C. Ng, Contrastive Retrieval, 2022."
        ),
        representative_output=_empty(),
        expected_min=0,
        expected_max=0,
        notes="Reference list — no technical content of its own.",
    ),
    # ------------------------------------------------------------ contribution
    DiscriminationChunk(
        chunk_id="contrib-gated-sparsifier",
        chunk_type="clear_contribution",
        text=(
            "We propose a learned token sparsifier that scores each token with a lightweight "
            "gating head and drops tokens below an adaptive percentile threshold before the "
            "attention layer. Unlike fixed top-k pruning, the threshold is recomputed per "
            "sequence from the gate score distribution, so short and long inputs keep different "
            "fractions of tokens. This reduces attention cost while preserving salient tokens."
        ),
        representative_output={
            "candidates": [
                {
                    "claim_text": (
                        "A learned token sparsifier that prunes tokens below a per-sequence "
                        "adaptive percentile of gating scores before attention."
                    ),
                    "problem": "Fixed pruning wastes capacity on variable-length inputs.",
                    "mechanism": (
                        "A gating head scores each token; an adaptive percentile threshold is "
                        "recomputed per sequence from the score distribution and prunes below it."
                    ),
                    "tech_field": "Machine learning",
                    "ipc_cpc_guess": "G06N 3/08",
                    "source_span": {"source_kind": "paper", "section_hint": None},
                }
            ]
        },
        expected_min=1,
        expected_max=1,
        notes='"We propose" plus a described mechanism — one contribution.',
    ),
    DiscriminationChunk(
        chunk_id="contrib-two-part-pipeline",
        chunk_type="clear_contribution",
        text=(
            "Our system contributes two components. First, we introduce a claim-chunker that "
            "splits a patent claim into independent limitation spans using a dependency-parse "
            "heuristic. Second, we design a cross-limitation aligner that matches limitation "
            "spans across two claims with a bipartite assignment over span embeddings. Together "
            "they enable limitation-level claim comparison."
        ),
        representative_output={
            "candidates": [
                {
                    "claim_text": (
                        "A claim-chunker that splits a patent claim into limitation spans via a "
                        "dependency-parse heuristic."
                    ),
                    "problem": "Whole-claim comparison hides limitation-level differences.",
                    "mechanism": (
                        "A dependency parse segments the claim into independent limitation spans."
                    ),
                    "tech_field": "Natural language processing",
                    "ipc_cpc_guess": "G06F 40/205",
                    "source_span": {"source_kind": "paper", "section_hint": None},
                },
                {
                    "claim_text": (
                        "A cross-limitation aligner that matches limitation spans between two "
                        "claims by bipartite assignment over span embeddings."
                    ),
                    "problem": "Limitation spans must be aligned across claims to compare them.",
                    "mechanism": (
                        "Span embeddings are matched with a bipartite assignment across the two "
                        "claims' limitation spans."
                    ),
                    "tech_field": "Natural language processing",
                    "ipc_cpc_guess": "G06F 40/30",
                    "source_span": {"source_kind": "paper", "section_hint": None},
                },
            ]
        },
        expected_min=2,
        expected_max=2,
        notes="Two distinct contributions — one candidate each, not merged.",
    ),
    DiscriminationChunk(
        chunk_id="contrib-code-ringbuffer",
        chunk_type="clear_contribution",
        text=(
            "class LockFreeRingBuffer:\n"
            "    # A single-producer single-consumer ring buffer that avoids locks by\n"
            "    # publishing the write index only after the slot payload is stored, using\n"
            "    # a release fence so the consumer never reads a torn slot.\n"
            "    def push(self, item):\n"
            "        slot = self._head & self._mask\n"
            "        self._slots[slot] = item\n"
            "        release_fence()\n"
            "        self._head += 1\n"
        ),
        representative_output={
            "candidates": [
                {
                    "claim_text": (
                        "A single-producer single-consumer lock-free ring buffer that publishes "
                        "the write index only after a release fence stores the slot payload."
                    ),
                    "problem": "Lock-based queues serialize producer and consumer.",
                    "mechanism": (
                        "The payload is written to the slot, a release fence is issued, then the "
                        "head index is advanced so the consumer never observes a torn slot."
                    ),
                    "tech_field": "Concurrent systems",
                    "ipc_cpc_guess": "G06F 5/06",
                    "source_span": {"source_kind": "code", "section_hint": "LockFreeRingBuffer"},
                }
            ]
        },
        expected_min=1,
        expected_max=1,
        notes="Code defines the mechanism directly — a contribution.",
    ),
    DiscriminationChunk(
        chunk_id="contrib-ordinary-but-own",
        chunk_type="clear_contribution",
        text=(
            "To deduplicate incoming documents we compute a SHA-256 hash of the normalized text "
            "and keep a first-seen set. Our normalization step, which we designed for this "
            "pipeline, lowercases, strips patent boilerplate headers, and collapses claim "
            "numbering before hashing so that reformatted copies of the same claim collide."
        ),
        representative_output={
            "candidates": [
                {
                    "claim_text": (
                        "A document normalization step that strips patent boilerplate headers and "
                        "collapses claim numbering before hashing to make reformatted copies "
                        "collide."
                    ),
                    "problem": "Reformatted duplicates escape naive content hashing.",
                    "mechanism": (
                        "Text is lowercased, boilerplate headers removed, and claim numbering "
                        "collapsed, then hashed so equivalent claims map to the same digest."
                    ),
                    "tech_field": "Information retrieval",
                    "ipc_cpc_guess": "G06F 16/215",
                    "source_span": {"source_kind": "paper", "section_hint": None},
                }
            ]
        },
        expected_min=1,
        expected_max=1,
        notes=(
            "Ordinary-looking but authored here. SHA-256 itself is off-the-shelf; the designed "
            "normalization is the contribution. Ordinariness is not a reason to drop it."
        ),
    ),
    # -------------------------------------------------------- applied off-the-shelf
    DiscriminationChunk(
        chunk_id="ots-standard-stack",
        chunk_type="applied_off_the_shelf",
        text=(
            "We use the standard BERT-base encoder without modification to embed claim text, "
            "index the vectors in an off-the-shelf FAISS flat index, and serve the API with the "
            "stock FastAPI framework. No changes were made to any of these components."
        ),
        representative_output=_empty(),
        expected_min=0,
        expected_max=0,
        notes="Every component is used as-is; nothing is contributed.",
    ),
    DiscriminationChunk(
        chunk_id="ots-imports-only",
        chunk_type="applied_off_the_shelf",
        text=(
            "import numpy as np\n"
            "from sklearn.linear_model import LogisticRegression\n"
            "\n"
            "# Fit a stock logistic-regression classifier with default hyperparameters.\n"
            "clf = LogisticRegression()\n"
            "clf.fit(X_train, y_train)\n"
            "preds = clf.predict(X_test)\n"
        ),
        representative_output=_empty(),
        expected_min=0,
        expected_max=0,
        notes="Calls an external library as a black box; defines no mechanism.",
    ),
    DiscriminationChunk(
        chunk_id="ots-pretrained-as-is",
        chunk_type="applied_off_the_shelf",
        text=(
            "For translation we call the pretrained NLLB-200 model through its published "
            "inference API and take the top hypothesis. We treat the model as a fixed black box "
            "and do not fine-tune or alter it in any way."
        ),
        representative_output=_empty(),
        expected_min=0,
        expected_max=0,
        notes="Pretrained model applied unchanged — not the text's contribution.",
    ),
    # ----------------------------------------------------------------- mixed
    DiscriminationChunk(
        chunk_id="mixed-ots-plus-adaptation",
        chunk_type="mixed",
        text=(
            "We embed claims with the off-the-shelf BERT-base encoder. On top of these frozen "
            "embeddings we introduce a claim-scope reweighting layer that rescales each token "
            "vector by an inferred limitation-importance weight before pooling, which we train "
            "with a contrastive objective over claim pairs. The encoder itself is left unchanged."
        ),
        representative_output={
            "candidates": [
                {
                    "claim_text": (
                        "A claim-scope reweighting layer that rescales frozen token embeddings by "
                        "an inferred limitation-importance weight before pooling."
                    ),
                    "problem": "Uniform pooling ignores which limitations carry claim scope.",
                    "mechanism": (
                        "Each token vector is multiplied by a learned limitation-importance "
                        "weight, trained with a contrastive objective over claim pairs, before "
                        "pooling."
                    ),
                    "tech_field": "Natural language processing",
                    "ipc_cpc_guess": "G06F 40/30",
                    "source_span": {"source_kind": "paper", "section_hint": None},
                }
            ]
        },
        expected_min=1,
        expected_max=1,
        notes="Drop the unchanged BERT encoder; keep the reweighting layer contribution.",
    ),
    DiscriminationChunk(
        chunk_id="mixed-background-then-contribution",
        chunk_type="mixed",
        text=(
            "Prior scheduling work uses fixed-priority queues [5] and weighted fair queueing [6]. "
            "In contrast, we propose a deadline-aware credit scheduler that grants each tenant a "
            "credit refill rate proportional to the tightness of its nearest deadline, so tenants "
            "approaching a deadline are dispatched ahead of slack-rich tenants without starving "
            "the latter."
        ),
        representative_output={
            "candidates": [
                {
                    "claim_text": (
                        "A deadline-aware credit scheduler that sets each tenant's credit refill "
                        "rate proportional to the tightness of its nearest deadline."
                    ),
                    "problem": "Static queueing ignores per-tenant deadline urgency.",
                    "mechanism": (
                        "Credit refill rate scales with deadline tightness so near-deadline "
                        "tenants dispatch ahead of slack-rich ones without starving them."
                    ),
                    "tech_field": "Distributed systems",
                    "ipc_cpc_guess": "G06F 9/48",
                    "source_span": {"source_kind": "paper", "section_hint": None},
                }
            ]
        },
        expected_min=1,
        expected_max=1,
        notes="Ignore the cited prior schedulers; keep the proposed credit scheduler.",
    ),
    DiscriminationChunk(
        chunk_id="mixed-two-contribs-amid-citations",
        chunk_type="mixed",
        text=(
            "Building on dense retrieval [8], our indexing pipeline adds two parts. We introduce "
            "a shard-router that assigns each claim to a shard by a learned locality hash, and a "
            "staleness-bounded refresh policy that re-embeds only shards whose drift score, "
            "measured against a rolling centroid, exceeds a threshold. Baseline HNSW [10] is used "
            "unchanged within each shard."
        ),
        representative_output={
            "candidates": [
                {
                    "claim_text": (
                        "A shard-router that assigns each claim to a shard using a learned "
                        "locality hash."
                    ),
                    "problem": "Uniform sharding scatters similar claims across shards.",
                    "mechanism": (
                        "A learned locality hash maps each claim to a shard so nearby claims "
                        "co-locate."
                    ),
                    "tech_field": "Information retrieval",
                    "ipc_cpc_guess": "G06F 16/22",
                    "source_span": {"source_kind": "paper", "section_hint": None},
                },
                {
                    "claim_text": (
                        "A staleness-bounded refresh policy that re-embeds only shards whose drift "
                        "score against a rolling centroid exceeds a threshold."
                    ),
                    "problem": "Re-embedding every shard on each update is wasteful.",
                    "mechanism": (
                        "Per-shard drift is measured against a rolling centroid and only shards "
                        "above a threshold are re-embedded."
                    ),
                    "tech_field": "Information retrieval",
                    "ipc_cpc_guess": "G06F 16/23",
                    "source_span": {"source_kind": "paper", "section_hint": None},
                },
            ]
        },
        expected_min=2,
        expected_max=2,
        notes="Two contributions kept; cited dense retrieval and unchanged HNSW dropped.",
    ),
]


CHUNKS_BY_ID: dict[str, DiscriminationChunk] = {c.chunk_id: c for c in DISCRIMINATION_CHUNKS}
