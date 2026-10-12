# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY global.json ./
COPY src/StudyPlanner.Domain/StudyPlanner.Domain.csproj src/StudyPlanner.Domain/
COPY src/StudyPlanner.Application/StudyPlanner.Application.csproj src/StudyPlanner.Application/
COPY src/StudyPlanner.Infrastructure/StudyPlanner.Infrastructure.csproj src/StudyPlanner.Infrastructure/
COPY src/StudyPlanner.Web/StudyPlanner.Web.csproj src/StudyPlanner.Web/
RUN dotnet restore src/StudyPlanner.Web/StudyPlanner.Web.csproj -p:NuGetAudit=true -p:NuGetAuditMode=all
COPY src/ src/
RUN dotnet publish src/StudyPlanner.Web/StudyPlanner.Web.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/keys /app/logs && chown -R app:app /app/keys /app/logs
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DataProtection__KeysPath=/app/keys
EXPOSE 8080
COPY --from=build /app/publish ./
USER $APP_UID
HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=5 \
    CMD curl --fail --silent http://127.0.0.1:8080/health/ready || exit 1
ENTRYPOINT ["dotnet", "StudyPlanner.Web.dll"]
