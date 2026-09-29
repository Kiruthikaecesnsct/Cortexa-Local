import asyncio
import logging
import os
import shutil
import tempfile
import uuid

from ingestion.domain.errors.parser_errors import ConversionError, ConversionTimeoutError

logger = logging.getLogger(__name__)


def _output_pdf_path(work_dir: str, input_path: str) -> str:
    base_name = os.path.splitext(os.path.basename(input_path))[0]
    return os.path.join(work_dir, f"{base_name}.pdf")


async def _run_soffice(input_path: str, work_dir: str, profile_dir: str, timeout_seconds: float):
    process = await asyncio.create_subprocess_exec(
        "soffice",
        "--headless",
        "--convert-to",
        "pdf",
        "--outdir",
        work_dir,
        f"-env:UserInstallation=file://{profile_dir}",
        input_path,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
    )
    try:
        _, stderr = await asyncio.wait_for(process.communicate(), timeout=timeout_seconds)
    except TimeoutError as exc:
        process.kill()
        await process.wait()
        raise ConversionTimeoutError(f"DOCX to PDF conversion exceeded {timeout_seconds}s") from exc
    return process.returncode, stderr


def _write_input(work_dir: str, data: bytes) -> str:
    input_path = os.path.join(work_dir, f"{uuid.uuid4().hex}.docx")
    with open(input_path, "wb") as f:
        f.write(data)
    return input_path


async def convert_docx_to_pdf(data: bytes, timeout_seconds: float) -> bytes:
    work_dir = tempfile.mkdtemp(prefix="docx-convert-")
    profile_dir = tempfile.mkdtemp(prefix="soffice-profile-")
    try:
        input_path = _write_input(work_dir, data)
        try:
            return_code, stderr = await _run_soffice(
                input_path, work_dir, profile_dir, timeout_seconds
            )
        except FileNotFoundError as exc:
            raise ConversionError("soffice executable not found") from exc

        if return_code != 0:
            raise ConversionError(
                f"soffice exited with code {return_code}: {stderr.decode(errors='replace')[:500]}"
            )

        output_path = _output_pdf_path(work_dir, input_path)
        if not os.path.isfile(output_path):
            raise ConversionError("soffice reported success but produced no PDF output")

        with open(output_path, "rb") as f:
            return f.read()
    finally:
        shutil.rmtree(work_dir, ignore_errors=True)
        shutil.rmtree(profile_dir, ignore_errors=True)
