# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore (cache-friendly): copy project files first
COPY TheDailyRSS.slnx ./
COPY src/TheDailyRSS.Shared/TheDailyRSS.Shared.csproj src/TheDailyRSS.Shared/
COPY src/TheDailyRSS.Client/TheDailyRSS.Client.csproj src/TheDailyRSS.Client/
COPY src/TheDailyRSS.Server/TheDailyRSS.Server.csproj src/TheDailyRSS.Server/
RUN dotnet restore src/TheDailyRSS.Server/TheDailyRSS.Server.csproj

# Build + publish the hosted server (bundles the WASM client into wwwroot)
COPY . .
RUN dotnet publish src/TheDailyRSS.Server/TheDailyRSS.Server.csproj -c Release -o /app --no-restore

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# libgssapi-krb5-2: lets Npgsql probe GSS auth without logging a noisy load error.
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080
ENV DataDir=/data
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "TheDailyRSS.Server.dll"]
