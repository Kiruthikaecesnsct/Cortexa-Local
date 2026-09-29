from dataclasses import dataclass, field

import httpx


@dataclass
class BatchFile:
    name: str
    content: bytes
    content_type: str = "application/pdf"


@dataclass
class CreateBatchOptions:
    batch_name: str
    engine: str
    ai_model: str
    seed_corpus_domain: str
    files: list[BatchFile] = field(default_factory=list)


class GatewayClient:
    def __init__(self, http: httpx.AsyncClient) -> None:
        self._http = http

    async def create_batch(self, options: CreateBatchOptions) -> dict:
        file_parts = [("files", (f.name, f.content, f.content_type)) for f in options.files]
        form_data = {
            "batch_name": options.batch_name,
            "engine": options.engine,
            "ai_model": options.ai_model,
            "seed_corpus_domain": options.seed_corpus_domain,
        }
        response = await self._http.post("/batches", files=file_parts, data=form_data)
        response.raise_for_status()
        return response.json()["data"]

    async def start_batch(self, batch_id: str) -> dict:
        response = await self._http.post("/batches/start", json={"BatchId": batch_id})
        response.raise_for_status()
        return response.json()["data"]

    async def get_status(self, batch_id: str) -> dict:
        response = await self._http.get(f"/batches/{batch_id}/status")
        response.raise_for_status()
        return response.json()["data"]

    async def get_results(self, batch_id: str) -> dict:
        response = await self._http.get(f"/batches/{batch_id}/results")
        response.raise_for_status()
        return response.json()["data"]

    async def create_and_start_batch(self, options: CreateBatchOptions) -> str:
        batch = await self.create_batch(options)
        batch_id = batch["batch_id"]
        await self.start_batch(batch_id)
        return batch_id
