from uuid import UUID

from fastapi import FastAPI
from fastapi.testclient import TestClient

from seeding.api.routes.seeding_routes import router
from seeding.domain.errors.seeding_errors import SeedingReportNotFoundError, StorageWriteError
from seeding.domain.models.landscape import LandscapeProvenance, LandscapeSourceFlags
from seeding.domain.models.seeding_result import (
    ConceptMapEntry,
    PriorArtMatch,
    SeedingOpportunity,
    SeedingResult,
)


class FakeSeedingReportRepository:
    def __init__(self) -> None:
        self.results: dict[str, SeedingResult] = {}
        self.should_raise_storage_error = False

    async def get_by_batch(self, batch_id: str) -> SeedingResult:
        if self.should_raise_storage_error:
            raise StorageWriteError("Simulated storage error")
        if batch_id not in self.results:
            raise SeedingReportNotFoundError(batch_id)
        return self.results[batch_id]

    async def save(self, result: SeedingResult) -> None:
        if self.should_raise_storage_error:
            raise StorageWriteError("Simulated storage error")
        self.results[result.batch_id] = result


def _make_seeding_result(batch_id: str = "batch-1") -> SeedingResult:
    opportunities = [
        SeedingOpportunity(
            id="cand-1-core",
            title="Core Invention",
            description="Core description",
            confidence_score=85.0,
            roadmap_alignment="Core",
        ),
        SeedingOpportunity(
            id="cand-1-continuation-0",
            title="Continuation 1",
            description="Continuation 1 description",
            confidence_score=85.0,
            roadmap_alignment="Continuation",
        ),
    ]
    return SeedingResult(id="result-1", batch_id=batch_id, opportunities=opportunities)


def _make_client_with_repository(repository: FakeSeedingReportRepository) -> TestClient:
    app = FastAPI()
    app.include_router(router, prefix="/seeding")
    app.state.seeding_report_repository = repository
    return TestClient(app, raise_server_exceptions=False)


def test_get_seeding_results_returns_200_when_batch_exists():
    repository = FakeSeedingReportRepository()
    seeding_result = _make_seeding_result(batch_id="batch-1")
    repository.results["batch-1"] = seeding_result
    client = _make_client_with_repository(repository)

    resp = client.get("/seeding/batches/batch-1/results")

    assert resp.status_code == 200
    body = resp.json()
    assert body["success"] is True
    assert "correlation_id" in body
    UUID(body["correlation_id"])
    assert "data" in body
    data = body["data"]
    assert data["id"] == "result-1"
    assert data["batch_id"] == "batch-1"
    assert len(data["opportunities"]) == 2
    assert data["opportunities"][0]["id"] == "cand-1-core"
    assert data["opportunities"][0]["title"] == "Core Invention"
    assert data["opportunities"][0]["confidence_score"] == 85.0
    assert data["opportunities"][0]["roadmap_alignment"] == "Core"
    assert "created_at" in data


def test_get_seeding_results_returns_404_when_batch_not_found():
    repository = FakeSeedingReportRepository()
    client = _make_client_with_repository(repository)

    resp = client.get("/seeding/batches/nonexistent-batch/results")

    assert resp.status_code == 404
    body = resp.json()
    assert body["success"] is False
    assert body["error_code"] == "SEEDING_RESULT_NOT_FOUND"
    assert "correlation_id" in body
    UUID(body["correlation_id"])


def test_get_seeding_results_correlation_id_is_uuid():
    repository = FakeSeedingReportRepository()
    seeding_result = _make_seeding_result(batch_id="batch-1")
    repository.results["batch-1"] = seeding_result
    client = _make_client_with_repository(repository)

    resp = client.get("/seeding/batches/batch-1/results")

    body = resp.json()
    correlation_id = body["correlation_id"]
    parsed_uuid = UUID(correlation_id)
    assert str(parsed_uuid) == correlation_id


def test_get_seeding_results_preserves_opportunity_fields():
    repository = FakeSeedingReportRepository()
    opportunity = SeedingOpportunity(
        id="test-id",
        title="Test Title",
        description="Test Description",
        confidence_score=92.5,
        roadmap_alignment="Platform",
    )
    seeding_result = SeedingResult(id="result-1", batch_id="batch-1", opportunities=[opportunity])
    repository.results["batch-1"] = seeding_result
    client = _make_client_with_repository(repository)

    resp = client.get("/seeding/batches/batch-1/results")

    body = resp.json()
    data = body["data"]
    opp_data = data["opportunities"][0]
    assert opp_data["id"] == "test-id"
    assert opp_data["title"] == "Test Title"
    assert opp_data["description"] == "Test Description"
    assert opp_data["confidence_score"] == 92.5
    assert opp_data["roadmap_alignment"] == "Platform"


def test_get_seeding_results_returns_200_for_legacy_report_without_v2_fields():
    legacy_doc = {
        "id": "legacy-result",
        "batch_id": "batch-legacy",
        "opportunities": [
            {
                "id": "legacy-opp",
                "title": "Legacy Opportunity",
                "description": "Legacy description",
                "confidence_score": 70.0,
                "roadmap_alignment": "Legacy",
            }
        ],
    }
    seeding_result = SeedingResult.model_validate(legacy_doc)
    repository = FakeSeedingReportRepository()
    repository.results["batch-legacy"] = seeding_result
    client = _make_client_with_repository(repository)

    resp = client.get("/seeding/batches/batch-legacy/results")

    assert resp.status_code == 200
    data = resp.json()["data"]
    assert data["engine"] == "seeding"
    assert data["document_id"] == ""
    assert data["landscape"] is None
    assert data["concept_map"] == []
    assert data["is_empty"] is False
    assert data["source_flags"]["corpus_only"] is True
    opp = data["opportunities"][0]
    assert opp["target_concept"] == ""
    assert opp["prior_art_proximity"] == []


def test_get_seeding_results_passes_through_v2_fields():
    opportunity = SeedingOpportunity(
        id="opp-1",
        title="Opportunity",
        description="Description",
        confidence_score=88.0,
        roadmap_alignment="Platform",
        target_concept="Memory",
        prior_art_proximity=[
            PriorArtMatch(
                reference="US-9",
                title="Prior art nine",
                url="http://p/9",
                source="USPTO",
                relevance_score=0.9,
            )
        ],
    )
    seeding_result = SeedingResult(
        id="result-v2",
        batch_id="batch-v2",
        document_id="doc-v2",
        opportunities=[opportunity],
        concept_map=[
            ConceptMapEntry(
                concept="Memory",
                density="dense",
                opportunity_ids=["opp-1"],
                whitespace=True,
            )
        ],
        landscape=LandscapeProvenance(
            landscape_id="landscape-v2",
            schema_version="1.0",
            source_flags=LandscapeSourceFlags(evidence_reachable=True, corpus_only=False),
        ),
        explanation="explained",
    )
    repository = FakeSeedingReportRepository()
    repository.results["batch-v2"] = seeding_result
    client = _make_client_with_repository(repository)

    resp = client.get("/seeding/batches/batch-v2/results")

    assert resp.status_code == 200
    data = resp.json()["data"]
    assert data["document_id"] == "doc-v2"
    assert data["explanation"] == "explained"
    assert data["landscape"]["landscape_id"] == "landscape-v2"
    assert data["source_flags"]["evidence_reachable"] is True
    assert data["source_flags"]["corpus_only"] is False
    assert data["concept_map"][0]["concept"] == "Memory"
    assert data["concept_map"][0]["opportunity_ids"] == ["opp-1"]
    assert data["concept_map"][0]["whitespace"] is True
    opp = data["opportunities"][0]
    assert opp["target_concept"] == "Memory"
    assert opp["prior_art_proximity"][0]["reference"] == "US-9"
    assert opp["prior_art_proximity"][0]["url"] == "http://p/9"


def test_get_seeding_results_returns_500_on_storage_error():
    repository = FakeSeedingReportRepository()
    repository.should_raise_storage_error = True
    client = _make_client_with_repository(repository)

    resp = client.get("/seeding/batches/batch-1/results")

    assert resp.status_code == 500
    body = resp.json()
    assert body["success"] is False
    assert body["error_code"] == "STORAGE_WRITE_ERROR"
    assert "message" in body
    assert "correlation_id" in body
    UUID(body["correlation_id"])
