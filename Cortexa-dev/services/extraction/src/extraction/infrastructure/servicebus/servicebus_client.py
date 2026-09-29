from azure.identity.aio import DefaultAzureCredential
from azure.servicebus.aio import ServiceBusClient

from extraction.infrastructure.config.settings import ExtractionSettings
from extraction.infrastructure.servicebus.rabbitmq_client import RabbitMqServiceBusClient


def get_servicebus_client(
    settings: ExtractionSettings, credential: DefaultAzureCredential
) -> ServiceBusClient | RabbitMqServiceBusClient:
    if settings.messaging_backend == "rabbitmq":
        return RabbitMqServiceBusClient(settings.rabbitmq_url)
    if settings.servicebus_connection_string:
        return ServiceBusClient.from_connection_string(settings.servicebus_connection_string)
    if not settings.servicebus_namespace_fqdn:
        raise ValueError("servicebus_namespace_fqdn is not configured.")
    return ServiceBusClient(
        fully_qualified_namespace=settings.servicebus_namespace_fqdn,
        credential=credential,
    )
