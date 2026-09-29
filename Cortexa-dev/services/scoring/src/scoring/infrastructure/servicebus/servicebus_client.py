from azure.identity.aio import DefaultAzureCredential
from azure.servicebus.aio import ServiceBusClient

from scoring.infrastructure.config.settings import ScoringSettings
from scoring.infrastructure.servicebus.rabbitmq_client import RabbitMqServiceBusClient


def get_servicebus_client(
    settings: ScoringSettings, credential: DefaultAzureCredential
) -> ServiceBusClient | RabbitMqServiceBusClient:
    if settings.messaging_backend == "rabbitmq":
        return RabbitMqServiceBusClient(settings.rabbitmq_url)
    # A configured connection string (local/emulator auth) takes precedence over managed identity.
    if settings.servicebus_connection_string:
        return ServiceBusClient.from_connection_string(settings.servicebus_connection_string)
    return ServiceBusClient(
        fully_qualified_namespace=settings.servicebus_namespace_fqdn,
        credential=credential,
    )
