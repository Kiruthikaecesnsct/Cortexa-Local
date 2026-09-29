import json
import logging

import pytest

from scoring.application.parsing.five_axis_parser import parse_five_axes
from scoring.domain.enums.scoring_axis import ScoringAxis
from scoring.domain.errors.scoring_errors import AxisParseError, UngroundedVerdictError
from scoring.domain.models.evidence_bundle import EvidenceBundle

VALID_REFS_ALL_SOURCES = ["E1", "E2", "E3"]

AXES = list(ScoringAxis)


def _build_axes_dict(refs: list[str] | None = None) -> dict:
    chosen_refs = refs if refs is not None else VALID_REFS_ALL_SOURCES
    return {axis.value: {"score": 80, "refs": chosen_refs} for axis in AXES}


@pytest.fixture()
def valid_axes_json(bundle_all_sources: EvidenceBundle) -> str:
    return json.dumps(_build_axes_dict())


class TestValidResponses:
    def test_parse_valid_response_all_axes(
        self, bundle_all_sources: EvidenceBundle, valid_axes_json: str
    ) -> None:
        verdict = parse_five_axes(valid_axes_json, bundle_all_sources)

        assert verdict is not None
        assert len(verdict.axes) == 5

    def test_parse_fenced_json_response(self, bundle_all_sources: EvidenceBundle) -> None:
        fenced = "```json\n" + json.dumps(_build_axes_dict()) + "\n```"

        verdict = parse_five_axes(fenced, bundle_all_sources)

        assert len(verdict.axes) == 5

    def test_parse_json_embedded_in_prose(self, bundle_all_sources: EvidenceBundle) -> None:
        prose = (
            "Here is the scoring result:\n" + json.dumps(_build_axes_dict()) + "\nEnd of output."
        )

        verdict = parse_five_axes(prose, bundle_all_sources)

        assert len(verdict.axes) == 5

    def test_all_axes_present_in_verdict(
        self, bundle_all_sources: EvidenceBundle, valid_axes_json: str
    ) -> None:
        verdict = parse_five_axes(valid_axes_json, bundle_all_sources)

        assert set(verdict.axes.keys()) == set(ScoringAxis)

    def test_score_is_numeric_and_in_range(
        self, bundle_all_sources: EvidenceBundle, valid_axes_json: str
    ) -> None:
        verdict = parse_five_axes(valid_axes_json, bundle_all_sources)

        for axis, axis_score in verdict.axes.items():
            assert isinstance(axis_score.score, int), f"{axis.value} score is not int"
            assert 0 <= axis_score.score <= 100, f"{axis.value} score out of range"

    def test_axis_with_citations_passes(
        self, bundle_all_sources: EvidenceBundle, valid_axes_json: str
    ) -> None:
        verdict = parse_five_axes(valid_axes_json, bundle_all_sources)

        for axis_score in verdict.axes.values():
            assert len(axis_score.refs) > 0

    def test_verdict_carries_bundle_ids(
        self, bundle_all_sources: EvidenceBundle, valid_axes_json: str
    ) -> None:
        verdict = parse_five_axes(valid_axes_json, bundle_all_sources)

        assert verdict.batch_id == bundle_all_sources.batch_id
        assert verdict.job_id == bundle_all_sources.job_id
        assert verdict.candidate_id == bundle_all_sources.candidate_id
        assert verdict.document_id == bundle_all_sources.document_id


class TestUngroundedErrors:
    def test_axis_missing_refs_raises_ungrounded(self, bundle_all_sources: EvidenceBundle) -> None:
        axes = _build_axes_dict()
        axes[ScoringAxis.Novelty.value]["refs"] = []
        raw = json.dumps(axes)

        with pytest.raises(UngroundedVerdictError) as exc_info:
            parse_five_axes(raw, bundle_all_sources)

        assert "Novelty" in str(exc_info.value)

    def test_axis_hallucinated_ref_raises_ungrounded(
        self, bundle_all_sources: EvidenceBundle
    ) -> None:
        axes = _build_axes_dict()
        axes[ScoringAxis.Inventiveness.value]["refs"] = ["E99"]
        raw = json.dumps(axes)

        with pytest.raises(UngroundedVerdictError):
            parse_five_axes(raw, bundle_all_sources)

    def test_rejection_is_logged(
        self, bundle_all_sources: EvidenceBundle, caplog: pytest.LogCaptureFixture
    ) -> None:
        axes = _build_axes_dict()
        axes[ScoringAxis.Commercial.value]["refs"] = []
        raw = json.dumps(axes)

        with caplog.at_level(
            logging.WARNING, logger="scoring.application.parsing.five_axis_parser"
        ):
            with pytest.raises(UngroundedVerdictError):
                parse_five_axes(raw, bundle_all_sources)

        assert "Commercial" in caplog.text


class TestParseErrors:
    def test_missing_axis_raises_parse_error(self, bundle_all_sources: EvidenceBundle) -> None:
        axes = _build_axes_dict()
        del axes[ScoringAxis.Novelty.value]
        raw = json.dumps(axes)

        with pytest.raises(AxisParseError) as exc_info:
            parse_five_axes(raw, bundle_all_sources)

        assert "Novelty" in str(exc_info.value)

    def test_invalid_score_type_raises_parse_error(
        self, bundle_all_sources: EvidenceBundle
    ) -> None:
        axes = _build_axes_dict()
        axes[ScoringAxis.Strategic.value]["score"] = "high"
        raw = json.dumps(axes)

        with pytest.raises(AxisParseError):
            parse_five_axes(raw, bundle_all_sources)

    def test_malformed_json_raises_parse_error(self, bundle_all_sources: EvidenceBundle) -> None:
        with pytest.raises(AxisParseError):
            parse_five_axes("not json at all", bundle_all_sources)

    def test_missing_refs_field_raises_parse_error(
        self, bundle_all_sources: EvidenceBundle
    ) -> None:
        axes = _build_axes_dict()
        del axes[ScoringAxis.Patentability.value]["refs"]
        raw = json.dumps(axes)

        with pytest.raises(AxisParseError) as exc_info:
            parse_five_axes(raw, bundle_all_sources)

        assert "refs" in str(exc_info.value)
