from dataclasses import dataclass

SPARSE = "sparse"
MODERATE = "moderate"
CROWDED = "crowded"

_RANK = {SPARSE: 0, MODERATE: 1, CROWDED: 2}


@dataclass(frozen=True)
class DensityThresholds:
    sim_floor: float
    crowded_max_sim: float
    moderate_max_sim: float
    crowded_corpus_hits: int
    moderate_corpus_hits: int
    crowded_live_hits: int
    moderate_live_hits: int
    whitespace_sim: float


def max_similarity(scores: list[float]) -> float:
    return max(scores, default=0.0)


def count_relevant(scores: list[float], sim_floor: float) -> int:
    return sum(1 for score in scores if score >= sim_floor)


def corpus_axis(scores: list[float], thresholds: DensityThresholds) -> str:
    max_sim = max_similarity(scores)
    relevant = count_relevant(scores, thresholds.sim_floor)
    if relevant >= thresholds.crowded_corpus_hits or max_sim >= thresholds.crowded_max_sim:
        return CROWDED
    if relevant >= thresholds.moderate_corpus_hits or max_sim >= thresholds.moderate_max_sim:
        return MODERATE
    return SPARSE


def live_axis(live_hit_count: int, thresholds: DensityThresholds) -> str:
    if live_hit_count >= thresholds.crowded_live_hits:
        return CROWDED
    if live_hit_count >= thresholds.moderate_live_hits:
        return MODERATE
    return SPARSE


def classify_concept(
    scores: list[float], live_hit_count: int, thresholds: DensityThresholds
) -> tuple[str, str, str]:
    corpus = corpus_axis(scores, thresholds)
    live = live_axis(live_hit_count, thresholds)
    density = corpus if _RANK[corpus] >= _RANK[live] else live
    return density, corpus, live


def whitespace_flag(scores: list[float], whitespace_sim: float) -> bool:
    return max_similarity(scores) < whitespace_sim
