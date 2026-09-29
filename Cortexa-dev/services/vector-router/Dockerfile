FROM python:3.14-slim AS builder
WORKDIR /app
RUN pip install uv
COPY pyproject.toml .
COPY uv.lock .
COPY src/ src/
RUN uv sync --no-dev

FROM python:3.14-slim AS final
WORKDIR /app
COPY --from=builder /app/.venv /app/.venv
COPY src/ src/
ENV PATH="/app/.venv/bin:$PATH"
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser
EXPOSE 8000
CMD ["uvicorn", "vector_router.main:app", "--host", "0.0.0.0", "--port", "8000"]
