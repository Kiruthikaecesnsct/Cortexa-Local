from azure.identity.aio import DefaultAzureCredential
from azure.servicebus.aio import ServiceBusClient

from scoring.infrastructure.config.settings import ScoringSettings


def get_servicebus_client(
    settings: ScoringSettings, credential: DefaultAzureCredential
) -> ServiceBusClient:
    # A configured connection string (local/emulator auth) takes precedence over managed identity.
    if settings.servicebus_connection_string:
        return ServiceBusClient.from_connection_string(settings.servicebus_connection_string)
    return ServiceBusClient(
        fully_qualified_namespace=settings.servicebus_namespace_fqdn,
        credential=credential,
    )
