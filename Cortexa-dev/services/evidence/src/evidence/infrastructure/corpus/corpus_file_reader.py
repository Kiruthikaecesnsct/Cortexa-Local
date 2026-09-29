import json
from pathlib import Path

from pydantic import ValidationError

from evidence.domain.errors.evidence_errors import CorpusLoadError
from evidence.domain.models.patent_corpus_record import PatentCorpusRecord


class CorpusFileReader:
    def __init__(self, file_path: str) -> None:
        self._file_path = file_path
        self._corpus_dir = Path(file_path).resolve().parent

    def read(self, file_name: str | None = None) -> list[PatentCorpusRecord]:
        path = self._resolve_path(file_name)
        if not path.is_file():
            raise CorpusLoadError(f"Corpus file not found: {path.name}")
        lines = path.read_text(encoding="utf-8").splitlines()
        return [
            self._parse_line(line, number)
            for number, line in enumerate(lines, start=1)
            if line.strip()
        ]

    def _resolve_path(self, file_name: str | None) -> Path:
        if file_name is None:
            return Path(self._file_path)
        candidate = (self._corpus_dir / Path(file_name).name).resolve()
        if candidate.parent != self._corpus_dir:
            raise CorpusLoadError(f"Corpus file name not permitted: {file_name}")
        return candidate

    def _parse_line(self, line: str, line_number: int) -> PatentCorpusRecord:
        try:
            raw = json.loads(line)
            return PatentCorpusRecord.model_validate(raw)
        except (json.JSONDecodeError, ValidationError) as exc:
            raise CorpusLoadError(f"Malformed corpus record at line {line_number}: {exc}") from exc
