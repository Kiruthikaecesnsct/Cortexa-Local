from azure.identity.aio import DefaultAzureCredential
from azure.servicebus.aio import ServiceBusClient

from extraction.infrastructure.config.settings import ExtractionSettings


def get_servicebus_client(
    settings: ExtractionSettings, credential: DefaultAzureCredential
) -> ServiceBusClient:
    if not settings.servicebus_namespace_fqdn:
        raise ValueError("servicebus_namespace_fqdn is not configured.")
    return ServiceBusClient(
        fully_qualified_namespace=settings.servicebus_namespace_fqdn,
        credential=credential,
    )
