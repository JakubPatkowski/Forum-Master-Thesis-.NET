# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore layer (cache-friendly): build config + all projects, then restore the host graph.
COPY backend/global.json backend/Directory.Build.props backend/Directory.Packages.props ./backend/
COPY backend/src ./backend/src
RUN dotnet restore backend/src/Bootstrap/Forum.Api/Forum.Api.csproj
RUN dotnet publish backend/src/Bootstrap/Forum.Api/Forum.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN useradd --uid 1000 --create-home appuser
COPY --from=build /app .
USER 1000
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0
ENTRYPOINT ["dotnet", "Forum.Api.dll"]
