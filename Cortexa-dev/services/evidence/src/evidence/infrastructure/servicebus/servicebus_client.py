from azure.identity.aio import DefaultAzureCredential
from azure.servicebus.aio import ServiceBusClient

from evidence.infrastructure.config.settings import EvidenceSettings


def get_servicebus_client(
    settings: EvidenceSettings, credential: DefaultAzureCredential
) -> ServiceBusClient:
    return ServiceBusClient(
        fully_qualified_namespace=settings.servicebus_namespace_fqdn,
        credential=credential,
    )
