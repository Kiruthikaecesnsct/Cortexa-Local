import json
import re
from datetime import UTC, datetime
from uuid import uuid4

from pydantic import ValidationError

from extraction.domain.errors.extraction_errors import CandidateParseError
from extraction.domain.models.chunk_input import ChunkInput
from extraction.domain.models.invention_candidate import InventionCandidate
from extraction.domain.value_objects.provenance_span import ProvenanceSpan


class CandidateParser:
    def parse(
        self,
        content: str,
        chunk: ChunkInput,
        document_id: str,
        batch_id: str,
    ) -> list[InventionCandidate]:
        raw = self._load_json(content)
        candidates_raw = self._extract_list(raw)
        return [self._build(c, chunk, document_id, batch_id) for c in candidates_raw]

    def _load_json(self, content: str) -> dict:
        cleaned = self._strip_fences(content)
        try:
            data = json.loads(cleaned)
        except json.JSONDecodeError as exc:
            raise CandidateParseError(f"LLM response is not valid JSON: {exc}") from exc
        if not isinstance(data, dict):
            raise CandidateParseError(f"Expected JSON object, got {type(data).__name__}")
        return data

    def _strip_fences(self, content: str) -> str:
        stripped = content.strip()
        fence_match = re.match(r"^\s*```(?:json)?\s*\n?([\s\S]*?)\n?```\s*$", stripped)
        if fence_match:
            return fence_match.group(1).strip()
        try:
            json.loads(stripped)
            return stripped
        except json.JSONDecodeError:
            pass
        obj_match = re.search(r"\{[\s\S]*\}", stripped)
        if obj_match:
            return obj_match.group(0)
        return stripped

    def _extract_list(self, data: dict) -> list[dict]:
        if "candidates" not in data:
            raise CandidateParseError("LLM response missing 'candidates' key")
        raw = data["candidates"]
        if not isinstance(raw, list):
            raise CandidateParseError("'candidates' must be a JSON array")
        return raw

    def _build(
        self,
        raw: dict,
        chunk: ChunkInput,
        document_id: str,
        batch_id: str,
    ) -> InventionCandidate:
        try:
            source_span = self._resolve_span(raw, chunk)
            return InventionCandidate(
                id=str(uuid4()),
                document_id=document_id,
                batch_id=batch_id,
                claim_text=raw["claim_text"],
                problem=raw["problem"],
                mechanism=raw["mechanism"],
                tech_field=raw["tech_field"],
                ipc_cpc_guess=raw.get("ipc_cpc_guess"),
                source_span=source_span,
                source_chunk_index=chunk.order_index,
                created_at=datetime.now(UTC),
            )
        except (AttributeError, KeyError, TypeError, ValidationError) as exc:
            raise CandidateParseError(f"Candidate field error: {exc}") from exc

    def _resolve_span(self, raw: dict, chunk: ChunkInput) -> ProvenanceSpan:
        span_raw = raw.get("source_span", {})
        chunk_section = chunk.source_span.section_hint
        llm_section = span_raw.get("section_hint")
        section_hint = chunk_section if chunk_section else llm_section
        return ProvenanceSpan(
            source_kind=span_raw.get("source_kind", chunk.source_span.source_kind),
            locator=chunk.source_span.locator,
            section_hint=section_hint,
            span_start=chunk.source_span.span_start,
            span_end=chunk.source_span.span_end,
            page_number=chunk.source_span.page_number,
            excerpt=chunk.source_span.excerpt,
        )
