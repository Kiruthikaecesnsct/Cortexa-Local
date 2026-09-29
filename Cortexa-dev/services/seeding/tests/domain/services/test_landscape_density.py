from seeding.domain.services.landscape_density import (
    CROWDED,
    MODERATE,
    SPARSE,
    DensityThresholds,
    classify_concept,
    corpus_axis,
    count_relevant,
    live_axis,
    max_similarity,
    whitespace_flag,
)


def _thresholds() -> DensityThresholds:
    return DensityThresholds(
        sim_floor=0.65,
        crowded_max_sim=0.82,
        moderate_max_sim=0.70,
        crowded_corpus_hits=10,
        moderate_corpus_hits=3,
        crowded_live_hits=15,
        moderate_live_hits=3,
        whitespace_sim=0.62,
    )


def test_corpus_axis_crowded_by_relevant_count():
    scores = [0.66] * 10

    assert corpus_axis(scores, _thresholds()) == CROWDED


def test_corpus_axis_crowded_by_max_similarity():
    scores = [0.5, 0.85]

    assert corpus_axis(scores, _thresholds()) == CROWDED


def test_corpus_axis_moderate_at_exact_hit_threshold():
    scores = [0.66, 0.66, 0.66]

    assert corpus_axis(scores, _thresholds()) == MODERATE


def test_corpus_axis_moderate_at_exact_max_similarity():
    scores = [0.70]

    assert corpus_axis(scores, _thresholds()) == MODERATE


def test_corpus_axis_sparse_when_below_thresholds():
    scores = [0.5, 0.6]

    assert corpus_axis(scores, _thresholds()) == SPARSE


def test_corpus_axis_sparse_on_empty_hits():
    assert corpus_axis([], _thresholds()) == SPARSE


def test_live_axis_crowded_moderate_sparse():
    thresholds = _thresholds()

    assert live_axis(15, thresholds) == CROWDED
    assert live_axis(3, thresholds) == MODERATE
    assert live_axis(2, thresholds) == SPARSE
    assert live_axis(0, thresholds) == SPARSE


def test_classify_concept_takes_max_axis_when_live_only_crowded():
    density, corpus, live = classify_concept([], 15, _thresholds())

    assert corpus == SPARSE
    assert live == CROWDED
    assert density == CROWDED


def test_classify_concept_takes_max_axis_when_corpus_dominates():
    density, corpus, live = classify_concept([0.85], 0, _thresholds())

    assert corpus == CROWDED
    assert live == SPARSE
    assert density == CROWDED


def test_count_relevant_and_max_similarity():
    scores = [0.4, 0.65, 0.9]

    assert count_relevant(scores, 0.65) == 2
    assert max_similarity(scores) == 0.9
    assert max_similarity([]) == 0.0


def test_whitespace_flag_below_threshold_is_whitespace():
    assert whitespace_flag([0.5, 0.61], 0.62) is True


def test_whitespace_flag_at_threshold_is_not_whitespace():
    assert whitespace_flag([0.62], 0.62) is False


def test_whitespace_flag_above_threshold_is_not_whitespace():
    assert whitespace_flag([0.7], 0.62) is False


def test_whitespace_flag_empty_hits_is_whitespace():
    assert whitespace_flag([], 0.62) is True
