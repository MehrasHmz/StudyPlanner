FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/StudyPlanner.Web/StudyPlanner.Web.csproj", "src/StudyPlanner.Web/"]
COPY ["src/StudyPlanner.Application/StudyPlanner.Application.csproj", "src/StudyPlanner.Application/"]
COPY ["src/StudyPlanner.Domain/StudyPlanner.Domain.csproj", "src/StudyPlanner.Domain/"]
COPY ["src/StudyPlanner.Infrastructure/StudyPlanner.Infrastructure.csproj", "src/StudyPlanner.Infrastructure/"]
RUN dotnet restore "src/StudyPlanner.Web/StudyPlanner.Web.csproj"
COPY . .
WORKDIR "/src/src/StudyPlanner.Web"
RUN dotnet build -c Release -o /app/build
FROM build AS publish
RUN dotnet publish -c Release -o /app/publish
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "StudyPlanner.Web.dll"]
