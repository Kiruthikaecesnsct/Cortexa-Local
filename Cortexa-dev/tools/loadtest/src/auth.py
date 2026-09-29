import httpx
from azure.identity import DefaultAzureCredential
from azure.keyvault.secrets import SecretClient

from settings import LoadTestSettings


def _read_kv_secret(kv_name: str, secret_name: str) -> str:
    credential = DefaultAzureCredential()
    client = SecretClient(
        vault_url=f"https://{kv_name}.vault.azure.net",
        credential=credential,
    )
    return client.get_secret(secret_name).value


async def fetch_access_token(settings: LoadTestSettings) -> str:
    email = _read_kv_secret(settings.key_vault_name, settings.admin_email_secret)
    password = _read_kv_secret(settings.key_vault_name, settings.admin_password_secret)

    base_url = str(settings.gateway_base_url).rstrip("/")
    async with httpx.AsyncClient(base_url=base_url, timeout=httpx.Timeout(60.0)) as client:
        response = await client.post(
            "/auth/login",
            json={"Email": email, "Password": password},
        )

    response.raise_for_status()
    return response.json()["data"]["access_token"]
