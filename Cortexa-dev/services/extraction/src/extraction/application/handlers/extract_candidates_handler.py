import logging

from extraction.application.dtos.model_complete_result import (
    DualModelCompleteResult,
    ModelCompleteResult,
)
from extraction.application.parsers.candidate_parser import CandidateParser
from extraction.application.prompts.extraction_prompt_builder import ExtractionPromptBuilder
from extraction.domain.enums.model_mode import ModelMode
from extraction.domain.models.chunk_input import ChunkInput
from extraction.domain.models.invention_candidate import InventionCandidate
from extraction.infrastructure.model_router.model_router_client import ModelRouterClient

logger = logging.getLogger(__name__)


class ExtractCandidatesHandler:
    def __init__(
        self,
        builder: ExtractionPromptBuilder,
        client: ModelRouterClient,
        parser: CandidateParser,
        default_mode: ModelMode,
    ) -> None:
        self._builder = builder
        self._client = client
        self._parser = parser
        self._default_mode = default_mode

    async def handle(
        self,
        chunk: ChunkInput,
        document_id: str,
        batch_id: str,
        document_context: str | None,
        mode: ModelMode | None = None,
        ai_model: str | None = None,
    ) -> list[InventionCandidate]:
        effective_mode = mode if mode is not None else self._default_mode
        prompt = self._builder.build(chunk, document_context)
        result = await self._client.call(prompt, effective_mode, ai_model)
        content = self._extract_content(result)
        finish_reason = self._extract_finish_reason(result)
        if finish_reason == "length":
            logger.warning(
                "chunk_index=%s document_id=%s batch_id=%s "
                "finish_reason=length — completion truncated at token limit",
                chunk.order_index,
                document_id,
                batch_id,
            )
        return self._parser.parse(content, chunk, document_id, batch_id)

    def _extract_content(self, result: ModelCompleteResult | DualModelCompleteResult) -> str:
        if isinstance(result, DualModelCompleteResult):
            return result.primary.content
        return result.content

    def _extract_finish_reason(
        self, result: ModelCompleteResult | DualModelCompleteResult
    ) -> str | None:
        if isinstance(result, DualModelCompleteResult):
            return result.primary.finish_reason
        return result.finish_reason
