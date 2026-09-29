from azure.identity.aio import DefaultAzureCredential
from azure.servicebus.aio import ServiceBusClient

from harvesting.infrastructure.config.settings import HarvestingSettings


def get_servicebus_client(
    settings: HarvestingSettings, credential: DefaultAzureCredential
) -> ServiceBusClient:
    return ServiceBusClient(
        fully_qualified_namespace=settings.servicebus_namespace_fqdn,
        credential=credential,
    )
