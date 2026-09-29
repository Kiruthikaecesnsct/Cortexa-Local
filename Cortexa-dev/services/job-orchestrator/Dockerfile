FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Build context is this service directory (services/job-orchestrator), so paths
# are relative to it — not the repo root.
COPY ["src/Api/Cortexa.JobOrchestrator.Api.csproj", "src/Api/"]
COPY ["src/Application/Cortexa.JobOrchestrator.Application.csproj", "src/Application/"]
COPY ["src/Domain/Cortexa.JobOrchestrator.Domain.csproj", "src/Domain/"]
COPY ["src/Infrastructure/Cortexa.JobOrchestrator.Infrastructure.csproj", "src/Infrastructure/"]
RUN dotnet restore "src/Api/Cortexa.JobOrchestrator.Api.csproj"

COPY . .
WORKDIR "/src/src/Api"
RUN dotnet build "Cortexa.JobOrchestrator.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Cortexa.JobOrchestrator.Api.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 5000
# Bind Kestrel to 5000 to match the Container Apps ingress targetPort. The .NET 8
# base image otherwise defaults to 8080, which fails the startup probe on 5000.
ENV ASPNETCORE_HTTP_PORTS=5000
COPY --from=publish /app/publish .
RUN adduser --disabled-password --gecos '' appuser && chown -R appuser /app
USER appuser
ENTRYPOINT ["dotnet", "Cortexa.JobOrchestrator.Api.dll"]
