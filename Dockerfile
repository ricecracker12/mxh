# syntax=docker/dockerfile:1
# Context build = gốc repo (mxh). Workflow dùng: context: .

# ---------- Build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Chỉ cần project API (+ các project nó tham chiếu) để restore/publish, không cần tests.
COPY . .
RUN dotnet restore src/SocialApp.Api/SocialApp.Api.csproj
RUN dotnet publish src/SocialApp.Api/SocialApp.Api.csproj \
      -c Release -o /app/publish \
      /p:UseAppHost=false

# ---------- Runtime stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# curl cho HEALTHCHECK trong compose (image aspnet KHÔNG có sẵn curl).
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Chạy non-root (image .NET 8 có sẵn user 'app', UID 1654).
USER app

# api service dùng entrypoint này; migrate service override thành ["... --migrate"].
ENTRYPOINT ["dotnet", "SocialApp.Api.dll"]
