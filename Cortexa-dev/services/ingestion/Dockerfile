FROM python:3.14-slim AS builder
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends git && rm -rf /var/lib/apt/lists/*
RUN pip install uv
COPY pyproject.toml .
COPY uv.lock .
COPY src/ src/
RUN uv sync --no-dev

FROM python:3.14-slim AS final
WORKDIR /app
# libreoffice-writer (headless, Writer component only) converts DOCX uploads to a
# canonical viewable PDF for geometry capture (US122). Installing only the writer
# component (not the full libreoffice meta-package) adds roughly 400-500MB to the
# final image versus ~1.5GB+ for the full suite (Calc/Impress/Draw are unused here).
RUN apt-get update && apt-get install -y --no-install-recommends git openssh-client libreoffice-writer && rm -rf /var/lib/apt/lists/*
COPY --from=builder /app/.venv /app/.venv
COPY src/ src/
ENV PATH="/app/.venv/bin:$PATH"
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser
EXPOSE 8000
CMD ["uvicorn", "ingestion.main:app", "--host", "0.0.0.0", "--port", "8000"]
