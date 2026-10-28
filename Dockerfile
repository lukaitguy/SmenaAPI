# Build: kontekst je folder sa .slnx fajlom.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Prvo samo .csproj fajlovi, da Docker kešira restore dok se zavisnosti ne promene.
COPY Smena.API.slnx ./
COPY Smena.Domain/Smena.Domain.csproj Smena.Domain/
COPY Smena.Application/Smena.Application.csproj Smena.Application/
COPY Smena.Infrastructure/Smena.Infrastructure.csproj Smena.Infrastructure/
COPY Smena.API/Smena.API.csproj Smena.API/
RUN dotnet restore Smena.API/Smena.API.csproj

COPY . .
RUN dotnet publish Smena.API/Smena.API.csproj -c Release -o /app --no-restore

# Migration bundle: samostalan izvršni fajl koji primenjuje migracije, bez SDK-a i izvornog koda.
# Gradi se za arhitekturu mašine na kojoj teče build (amd64 ili arm64).
FROM build AS bundle
ARG TARGETARCH
RUN dotnet tool install --global dotnet-ef --version "10.*"
ENV PATH="$PATH:/root/.dotnet/tools"
RUN RID=$([ "$TARGETARCH" = "arm64" ] && echo linux-arm64 || echo linux-x64) \
    && dotnet ef migrations bundle \
        --project Smena.Infrastructure --startup-project Smena.API \
        --self-contained -r "$RID" --force -o /bundle/efbundle

# Cilj `migrate`: docker build --target migrate .
# Konekcioni string ide kroz env var ConnectionStrings__Smena (bundle diže host aplikacije,
# pa ga traži pre nego što pročita --connection).
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS migrate
WORKDIR /bundle
COPY --from=bundle /bundle/efbundle .
USER $APP_UID
ENTRYPOINT ["./efbundle"]

# Podrazumevani cilj: sam API.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Npgsql pri konekciji traži Kerberos biblioteku; bez nje upozorava u logu.
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "Smena.API.dll"]
