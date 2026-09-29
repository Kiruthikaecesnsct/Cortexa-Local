from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from ingestion.application.parsers.docx_converter import convert_docx_to_pdf
from ingestion.domain.errors.parser_errors import ConversionError, ConversionTimeoutError


def _mock_process(returncode: int = 0, stderr: bytes = b""):
    process = MagicMock()
    process.returncode = returncode
    process.communicate = AsyncMock(return_value=(b"", stderr))
    process.kill = MagicMock()
    process.wait = AsyncMock()
    return process


def _write_output_pdf(work_dir_holder: dict, content: bytes = b"%PDF-1.4 fake") -> None:
    import os

    input_path = work_dir_holder["input_path"]
    work_dir = os.path.dirname(input_path)
    base_name = os.path.splitext(os.path.basename(input_path))[0]
    with open(os.path.join(work_dir, f"{base_name}.pdf"), "wb") as f:
        f.write(content)


async def test_convert_docx_to_pdf_success_returns_pdf_bytes():
    captured = {}

    async def fake_create_subprocess_exec(*args, **kwargs):
        input_path = args[-1]
        captured["input_path"] = input_path
        _write_output_pdf(captured)
        return _mock_process(returncode=0)

    with patch(
        "ingestion.application.parsers.docx_converter.asyncio.create_subprocess_exec",
        side_effect=fake_create_subprocess_exec,
    ):
        result = await convert_docx_to_pdf(b"fake docx bytes", timeout_seconds=5.0)

    assert result == b"%PDF-1.4 fake"


async def test_convert_docx_to_pdf_nonzero_exit_raises_conversion_error():
    async def fake_create_subprocess_exec(*args, **kwargs):
        return _mock_process(returncode=1, stderr=b"soffice blew up")

    with patch(
        "ingestion.application.parsers.docx_converter.asyncio.create_subprocess_exec",
        side_effect=fake_create_subprocess_exec,
    ):
        with pytest.raises(ConversionError, match="exited with code 1"):
            await convert_docx_to_pdf(b"fake docx bytes", timeout_seconds=5.0)


async def test_convert_docx_to_pdf_missing_output_raises_conversion_error():
    async def fake_create_subprocess_exec(*args, **kwargs):
        return _mock_process(returncode=0)

    with patch(
        "ingestion.application.parsers.docx_converter.asyncio.create_subprocess_exec",
        side_effect=fake_create_subprocess_exec,
    ):
        with pytest.raises(ConversionError, match="no PDF output"):
            await convert_docx_to_pdf(b"fake docx bytes", timeout_seconds=5.0)


async def test_convert_docx_to_pdf_timeout_kills_process_and_raises_typed_error():
    process = _mock_process()

    async def fake_create_subprocess_exec(*args, **kwargs):
        return process

    with (
        patch(
            "ingestion.application.parsers.docx_converter.asyncio.create_subprocess_exec",
            side_effect=fake_create_subprocess_exec,
        ),
        patch(
            "ingestion.application.parsers.docx_converter.asyncio.wait_for",
            side_effect=TimeoutError(),
        ),
    ):
        with pytest.raises(ConversionTimeoutError):
            await convert_docx_to_pdf(b"fake docx bytes", timeout_seconds=0.01)

    process.kill.assert_called_once()


async def test_convert_docx_to_pdf_missing_soffice_raises_conversion_error():
    with patch(
        "ingestion.application.parsers.docx_converter.asyncio.create_subprocess_exec",
        side_effect=FileNotFoundError("soffice not found"),
    ):
        with pytest.raises(ConversionError, match="soffice executable not found"):
            await convert_docx_to_pdf(b"fake docx bytes", timeout_seconds=5.0)
