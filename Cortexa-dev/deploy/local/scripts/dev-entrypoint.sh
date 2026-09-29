#!/bin/sh
# Shared dev-container entrypoint for the local preview stack. Bind-mounted
# (not baked into any image) by deploy/local/docker-compose.preview.yml into
# every service that talks to the local Cosmos DB emulator directly, alongside
# a read-only mount of deploy/local/certs/.
#
# The Cosmos DB Linux emulator (mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator)
# only serves the NoSQL API over HTTPS with a self-signed certificate it
# regenerates on every start; the .NET SDK refuses HTTP against it outright,
# and the Python SDK validates the cert like any other HTTPS endpoint. Neither
# client swaps this for a code change - the officially documented fix is to
# import the emulator's certificate into the OS trust store:
# https://learn.microsoft.com/azure/cosmos-db/how-to-develop-emulator#import-the-emulators-tlsssl-certificate
#
# deploy/local/preview.sh / preview.ps1 fetch that certificate into
# deploy/local/certs/cosmos-emulator.pem once the emulator reports healthy,
# before starting any service that depends on it - so by the time this script
# runs, the file is already there.
set -eu

if [ -f /certs/cosmos-emulator.pem ] && [ -d /usr/local/share/ca-certificates ]; then
    cp /certs/cosmos-emulator.pem /usr/local/share/ca-certificates/cosmos-emulator.crt
    update-ca-certificates >/dev/null 2>&1 || true
fi

exec "$@"
