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
RUN apt-get update && apt-get install -y --no-install-recommends git && rm -rf /var/lib/apt/lists/*
COPY --from=builder /app/.venv /app/.venv
COPY src/ src/
ENV PATH="/app/.venv/bin:$PATH"
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser
EXPOSE 8000
CMD ["uvicorn", "evidence.main:app", "--host", "0.0.0.0", "--port", "8000"]
