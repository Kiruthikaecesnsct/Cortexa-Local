import asyncio

from azure.cosmos.aio import ContainerProxy
from azure.cosmos.exceptions import CosmosHttpResponseError

from harvesting.domain.errors.harvesting_errors import (
    ReportNotFoundError,
    StoragePermanentError,
    StorageWriteError,
)
from harvesting.domain.models.harvesting_report import HarvestingReport

_PERMANENT_STATUS_CODES = {400, 413}
_TRANSIENT_STATUS_CODES = {408, 429, 503}


class CosmosReportRepository:
    def __init__(self, container: ContainerProxy, write_concurrency: int = 4) -> None:
        self._container = container
        self._write_concurrency = write_concurrency

    async def save(self, report: HarvestingReport) -> None:
        try:
            candidate_items = self._build_candidate_items(report)
            await self._upsert_candidates(candidate_items)
            header_item = self._build_header_item(report)
            await self._container.upsert_item(header_item)
        except CosmosHttpResponseError as exc:
            self._classify_and_raise(exc)

    def _build_candidate_items(self, report: HarvestingReport) -> list[dict]:
        items = []
        for candidate in report.candidates:
            candidate_dict = candidate.model_dump(mode="json")
            item = {
                "id": f"report_candidate:{report.batch_id}:{candidate.candidate_id}",
                "batch_id": report.batch_id,
                "engine": "harvesting",
                "doc_type": "report_candidate",
                "report_id": report.id,
                **candidate_dict,
            }
            items.append(item)
        return items

    def _build_header_item(self, report: HarvestingReport) -> dict:
        return {
            "id": report.id,
            "batch_id": report.batch_id,
            "document_id": report.document_id,
            "engine": "harvesting",
            "doc_type": "report_header",
            "generated_at": report.generated_at.isoformat(),
            "candidate_count": len(report.candidates),
        }

    async def _upsert_candidates(self, candidate_items: list[dict]) -> None:
        if not candidate_items:
            return
        sem = asyncio.Semaphore(self._write_concurrency)

        async def _upsert_one(item: dict) -> None:
            async with sem:
                await self._container.upsert_item(item)

        tasks = [_upsert_one(item) for item in candidate_items]
        await asyncio.gather(*tasks)

    def _classify_and_raise(self, exc: CosmosHttpResponseError) -> None:
        status = getattr(exc, "status_code", None)
        if status in _PERMANENT_STATUS_CODES:
            raise StoragePermanentError(str(exc)) from exc
        raise StorageWriteError(str(exc)) from exc

    async def get_by_batch(self, batch_id: str) -> HarvestingReport:
        query = (
            "SELECT * FROM c WHERE c.batch_id = @batch_id "
            "AND (NOT IS_DEFINED(c.engine) OR c.engine != 'seeding')"
        )
        parameters = [{"name": "@batch_id", "value": batch_id}]
        try:
            items = [
                item
                async for item in self._container.query_items(
                    query=query, parameters=parameters, partition_key=batch_id
                )
            ]
        except CosmosHttpResponseError as exc:
            self._classify_and_raise(exc)

        if not items:
            raise ReportNotFoundError(batch_id)

        return self._reassemble_report(items, batch_id)

    def _reassemble_report(self, items: list[dict], batch_id: str) -> HarvestingReport:
        if self._is_legacy_report(items):
            return HarvestingReport.model_validate(items[0])

        header = self._extract_header(items, batch_id)
        candidates = self._extract_candidates(items)
        candidates = sorted(candidates, key=lambda c: c["rank"])

        return HarvestingReport(
            id=header["id"],
            batch_id=header["batch_id"],
            document_id=header["document_id"],
            engine=header.get("engine", "harvesting"),
            generated_at=header["generated_at"],
            candidates=[
                self._build_candidate_model(
                    c, ["id", "batch_id", "engine", "doc_type", "report_id"]
                )
                for c in candidates
            ],
        )

    def _is_legacy_report(self, items: list[dict]) -> bool:
        if len(items) != 1:
            return False
        first = items[0]
        return "candidates" in first and "doc_type" not in first

    def _extract_header(self, items: list[dict], batch_id: str) -> dict:
        headers = [i for i in items if i.get("doc_type") == "report_header"]
        if not headers:
            raise ReportNotFoundError(batch_id)
        return headers[0]

    def _extract_candidates(self, items: list[dict]) -> list[dict]:
        return [i for i in items if i.get("doc_type") == "report_candidate"]

    def _build_candidate_model(self, item: dict, exclude_keys: list[str]) -> dict:
        return {k: v for k, v in item.items() if k not in exclude_keys}
