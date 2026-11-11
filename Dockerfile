# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, as its own layer: this only re-runs when the project file or
# the pinned SDK changes, not on every source edit.
COPY global.json simpleShare_backend.csproj ./
RUN dotnet restore simpleShare_backend.csproj

COPY . .
RUN dotnet publish simpleShare_backend.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:8080

EXPOSE 8080

COPY --from=build /app/publish .

# Created before the USER switch and handed to that uid, so a named volume
# mounted here inherits the ownership instead of coming up root-owned - which
# the app cannot write to, and Data Protection keys would be lost on restart.
RUN mkdir -p /app/data-protection-keys && chown $APP_UID /app/data-protection-keys

# APP_UID is defined by the base image (non-root, uid 1654).
USER $APP_UID

ENTRYPOINT ["dotnet", "simpleShare_backend.dll"]