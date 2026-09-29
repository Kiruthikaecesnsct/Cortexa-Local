from scoring.domain.enums.agreement_level import AgreementLevel
from scoring.domain.enums.scoring_axis import ScoringAxis
from scoring.domain.models.scoring_verdict import ScoringVerdict

_FULL_AGREEMENT_COUNT = 5
_PARTIAL_MIN_COUNT = 3
_TOLERANCE = 10


def _axes_agree(primary: ScoringVerdict, secondary: ScoringVerdict, axis: ScoringAxis) -> bool:
    return abs(primary.axes[axis].score - secondary.axes[axis].score) <= _TOLERANCE


def _count_agreeing_axes(primary: ScoringVerdict, secondary: ScoringVerdict) -> int:
    return sum(_axes_agree(primary, secondary, axis) for axis in ScoringAxis)


def _map_count_to_level(count: int) -> AgreementLevel:
    if count == _FULL_AGREEMENT_COUNT:
        return AgreementLevel.Full
    if count >= _PARTIAL_MIN_COUNT:
        return AgreementLevel.Partial
    return AgreementLevel.NoAgreement


def compute_agreement(
    primary: ScoringVerdict, secondary: ScoringVerdict
) -> tuple[AgreementLevel, int]:
    count = _count_agreeing_axes(primary, secondary)
    return _map_count_to_level(count), count
