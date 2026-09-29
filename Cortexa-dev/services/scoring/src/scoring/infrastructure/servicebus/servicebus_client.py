from azure.identity.aio import DefaultAzureCredential
from azure.servicebus.aio import ServiceBusClient

from scoring.infrastructure.config.settings import ScoringSettings


def get_servicebus_client(
    settings: ScoringSettings, credential: DefaultAzureCredential
) -> ServiceBusClient:
    return ServiceBusClient(
        fully_qualified_namespace=settings.servicebus_namespace_fqdn,
        credential=credential,
    )
