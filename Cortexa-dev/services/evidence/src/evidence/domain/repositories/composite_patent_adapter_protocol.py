from typing import Protocol

from evidence.domain.enums.patent_source_name import PatentSourceName
from evidence.domain.models.patent_match import PatentMatch
from evidence.domain.models.patent_source_result import PatentSourceResult


class CompositePatentAdapterProtocol(Protocol):
    async def search(
        self,
        query: str,
        limit: int,
        enabled: frozenset[PatentSourceName] | None = None,
    ) -> tuple[list[PatentMatch], list[PatentSourceResult]]: ...
