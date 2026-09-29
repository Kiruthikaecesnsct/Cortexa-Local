from scoring.domain.enums.scoring_axis import ScoringAxis

_AXIS_NAME_MAP: dict[ScoringAxis, str] = {
    ScoringAxis.Novelty: "novelty",
    ScoringAxis.Inventiveness: "non_obviousness",
    ScoringAxis.Patentability: "claim_clarity",
    ScoringAxis.Strategic: "enablement",
    ScoringAxis.Commercial: "utility",
}


def axis_name_for_frontend(axis: ScoringAxis) -> str:
    return _AXIS_NAME_MAP[axis]


def axis_reasoning_for(axis: ScoringAxis, score: int, refs: list[str]) -> str:
    axis_display = _AXIS_NAME_MAP[axis]
    return f"{axis_display} scored {score}/100, grounded in {len(refs)} evidence citation(s)."
