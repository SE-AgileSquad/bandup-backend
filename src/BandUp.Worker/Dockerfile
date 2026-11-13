# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props BandUp.sln ./
COPY src/BandUp.Domain/BandUp.Domain.csproj src/BandUp.Domain/
COPY src/BandUp.Contracts/BandUp.Contracts.csproj src/BandUp.Contracts/
COPY src/BandUp.Application/BandUp.Application.csproj src/BandUp.Application/
COPY src/BandUp.Infrastructure/BandUp.Infrastructure.csproj src/BandUp.Infrastructure/
COPY src/BandUp.Worker/BandUp.Worker.csproj src/BandUp.Worker/
RUN dotnet restore src/BandUp.Worker/BandUp.Worker.csproj

COPY src/ src/
RUN dotnet publish src/BandUp.Worker/BandUp.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN adduser --disabled-password --gecos "" --uid 5678 appuser
USER appuser

COPY --from=build /app .

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0

ENTRYPOINT ["dotnet", "BandUp.Worker.dll"]
