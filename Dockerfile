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

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "Smena.API.dll"]
