import httpx
import pytest

from settings import IntegrationSettings
from utils.auth import fetch_access_token
from utils.gateway_client import BatchFile, CreateBatchOptions, GatewayClient
from utils.poller import PollConfig, wait_for_batch_complete

_MINIMAL_PDF = b"""%PDF-1.4
1 0 obj<</Type /Catalog /Pages 2 0 R>>endobj
2 0 obj<</Type /Pages /Kids [3 0 R] /Count 1>>endobj
3 0 obj<</Type /Page /Parent 2 0 R /MediaBox [0 0 612 792]
/Contents 4 0 R /Resources<</Font<</F1 5 0 R>>>>>>endobj
4 0 obj<</Length 44>>
stream
BT /F1 12 Tf 100 700 Td (Hello  World) Tj ET
endstream
endobj
5 0 obj<</Type /Font /Subtype /Type1 /BaseFont /Helvetica>>endobj
xref
0 6
0000000000 65535 f\r
0000000009 00000 n\r
0000000058 00000 n\r
0000000115 00000 n\r
0000000266 00000 n\r
0000000360 00000 n\r
trailer<</Size 6 /Root 1 0 R>>
startxref
441
%%EOF"""


@pytest.fixture(scope="session")
def settings() -> IntegrationSettings:
    return IntegrationSettings()


@pytest.fixture(scope="session")
async def access_token(settings: IntegrationSettings) -> str:
    return await fetch_access_token(settings)


@pytest.fixture(scope="session")
async def authed_client(settings: IntegrationSettings, access_token: str):
    base_url = str(settings.gateway_base_url).rstrip("/")
    async with httpx.AsyncClient(
        base_url=base_url,
        headers={"Authorization": f"Bearer {access_token}"},
        timeout=httpx.Timeout(30.0),
    ) as client:
        yield client


@pytest.fixture(scope="session")
def gateway(authed_client: httpx.AsyncClient) -> GatewayClient:
    return GatewayClient(authed_client)


@pytest.fixture(scope="session")
async def live_batch_id(gateway: GatewayClient, settings: IntegrationSettings) -> str:
    options = CreateBatchOptions(
        batch_name="cortexa-integration-suite",
        engine="dual",
        ai_model=settings.default_ai_model,
        seed_corpus_domain="software",
        files=[BatchFile(name="test_paper.pdf", content=_MINIMAL_PDF)],
    )
    batch_id = await gateway.create_and_start_batch(options)

    config = PollConfig(
        timeout=float(settings.pipeline_timeout_s),
        interval=float(settings.poll_interval_s),
    )
    final_status = await wait_for_batch_complete(
        fetch_status=lambda: gateway.get_status(batch_id),
        config=config,
    )

    if final_status.get("status") == "Failed":
        pytest.fail(f"Pipeline batch {batch_id} failed completely — no results to test against.")

    return batch_id
