# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY WebhookInbox.sln ./
COPY src/WebhookInbox.Worker/WebhookInbox.Worker.csproj src/WebhookInbox.Worker/
COPY src/WebhookInbox.Infrastructure/WebhookInbox.Infrastructure.csproj src/WebhookInbox.Infrastructure/
COPY src/WebhookInbox.Domain/WebhookInbox.Domain.csproj src/WebhookInbox.Domain/
COPY src/WebhookInbox.Api/WebhookInbox.Api.csproj src/WebhookInbox.Api/
COPY src/WebhookInbox.UI/WebhookInbox.UI.csproj src/WebhookInbox.UI/
COPY tests/WebhookInbox.IntegrationTests/WebhookInbox.IntegrationTests.csproj tests/WebhookInbox.IntegrationTests/

RUN dotnet restore WebhookInbox.sln

COPY . .
RUN dotnet publish src/WebhookInbox.Worker/WebhookInbox.Worker.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "WebhookInbox.Worker.dll"]
