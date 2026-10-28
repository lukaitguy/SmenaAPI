# Smena API

Backend za Smena, aplikaciju za upravljanje smenama zaposlenih u restoranima. ASP.NET Core (.NET 10) API sa PostgreSQL bazom.

## Arhitektura

Slojevita (clean) arhitektura:

| Projekat | Uloga |
|---|---|
| `Smena.Domain` | Entiteti i enumi (Restaurant, Position, Employee, Invitation) |
| `Smena.Application` | Slučajevi korišćenja, validacija (FluentValidation), apstrakcije |
| `Smena.Infrastructure` | EF Core + Npgsql, ASP.NET Identity, JWT i refresh tokeni |
| `Smena.API` | Kontroleri, autorizacione politike, konfiguracija |

## Pokretanje

### Docker Compose (preporučeno)

```bash
docker compose up --build
```

- API: http://localhost:8080
- PostgreSQL: `localhost:5432` (baza `smena`, korisnik `smena`, lozinka `smena_dev`)

Migracije se primenjuju automatski pri startu (`Database__MigrateOnStartup=true`).

Zaustavljanje: `docker compose down` (dodaj `-v` da obrišeš i podatke baze).

### Lokalno

Potrebni su .NET 10 SDK i PostgreSQL. Podrazumevani konekcioni string je u `Smena.API/appsettings.Development.json`.

```bash
docker compose up -d db
dotnet run --project Smena.API
```

API će slušati na http://localhost:5287.

## Testovi

```bash
docker compose up -d db
dotnet test Smena.Tests
```

Domenski testovi rade bez baze. Integracioni testovi dižu ceo API nad pravim PostgreSQL-om, u zasebnoj bazi `smena_tests` (kreira se i migrira automatski). Drugi server se zadaje promenljivom okruženja `SMENA_TEST_CONNECTION`.

## Konfiguracija

| Ključ | Opis |
|---|---|
| `ConnectionStrings:Smena` | PostgreSQL konekcioni string |
| `Jwt:SigningKey` | Tajni ključ za potpisivanje JWT-a (obavezno postaviti u produkciji) |
| `Jwt:AccessTokenMinutes` / `Jwt:RefreshTokenDays` | Trajanje tokena (15 min / 14 dana) |
| `App:FrontendUrl` | URL frontenda |
| `Database:MigrateOnStartup` | Automatska primena migracija |

U Docker okruženju se ključevi prosleđuju kao environment varijable sa `__` umesto `:` (npr. `ConnectionStrings__Smena`).

## Endpointi

| Metoda | Ruta | Pristup |
|---|---|---|
| POST | `/api/auth/register` | javno, registracija restorana |
| POST | `/api/auth/login` | javno |
| POST | `/api/auth/refresh` | refresh token (cookie) |
| POST | `/api/auth/logout` | |
| GET | `/api/auth/me` | prijavljen korisnik |
| POST | `/api/invitations` | Manager/Owner, kreiranje pozivnice |
| GET | `/api/invitations` | Manager/Owner, lista pozivnica |
| DELETE | `/api/invitations/{id}` | Manager/Owner, povlačenje pozivnice |
| POST | `/api/invitations/preview` | javno, pregled pozivnice |
| POST | `/api/invitations/accept` | javno, prihvatanje pozivnice |
| GET | `/api/positions` | lista pozicija |

Primeri zahteva su u `Smena.API/SmenaAPI.http`.

## Uloge

`Staff`, `Manager` i `Owner`. Uloga je dozvola u sistemu i nezavisna je od pozicije zaposlenog u smeni.
