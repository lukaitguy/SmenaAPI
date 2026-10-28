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
- Mailpit (web pregled poslatih mejlova): http://localhost:8025

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
| `App:FrontendUrl` | URL frontenda, iz njega se prave linkovi u mejlovima |
| `Email:Host`, `Email:Port`, `Email:Username`, `Email:Password`, `Email:UseTls` | SMTP server za slanje mejlova. Ako `Email:Host` nije podešen, mejlovi se ne šalju (u log ide samo primalac i naslov). |
| `Email:FromAddress`, `Email:FromName` | Pošiljalac mejlova |
| `Database:MigrateOnStartup` | Automatska primena migracija |

U Docker okruženju se ključevi prosleđuju kao environment varijable sa `__` umesto `:` (npr. `ConnectionStrings__Smena`).

## Endpointi

| Metoda | Ruta | Pristup |
|---|---|---|
| POST | `/api/auth/register` | javno, registracija restorana |
| POST | `/api/auth/login` | javno |
| POST | `/api/auth/refresh` | refresh token (cookie) |
| POST | `/api/auth/logout` | |
| POST | `/api/auth/forgot-password` | javno, šalje link za reset lozinke (uvek 204) |
| POST | `/api/auth/reset-password` | javno, `email` + `token` iz linka + `newPassword` |
| GET | `/api/auth/me` | prijavljen korisnik |
| POST | `/api/invitations` | Manager/Owner, kreiranje pozivnice; šalje mejl i vraća link (`emailSent` kaže da li je slanje uspelo) |
| GET | `/api/invitations` | Manager/Owner, lista pozivnica |
| DELETE | `/api/invitations/{id}` | Manager/Owner, povlačenje pozivnice |
| POST | `/api/invitations/preview` | javno, pregled pozivnice |
| POST | `/api/invitations/accept` | javno, prihvatanje pozivnice |
| GET | `/api/positions` | lista pozicija |
| POST | `/api/positions` | Manager/Owner, nova pozicija |
| PUT | `/api/positions/{id}` | Manager/Owner, izmena imena |
| DELETE | `/api/positions/{id}` | Manager/Owner, brisanje (409 ako se koristi u smenama) |
| GET | `/api/employees` | Manager/Owner, lista zaposlenih |
| GET | `/api/employees/{id}` | Manager/Owner |
| PUT | `/api/employees/{id}` | Manager/Owner, ime, uloga i pozicije |
| PUT | `/api/employees/{id}/status` | Manager/Owner, aktivacija/deaktivacija |
| GET | `/api/shifts?from=&to=&employeeId=` | Manager/Owner, raspored za period (najviše 62 dana) |
| GET | `/api/shifts/mine?from=&to=` | smene prijavljenog zaposlenog |
| POST | `/api/shifts` | Manager/Owner, nova smena |
| PUT | `/api/shifts/{id}` | Manager/Owner, izmena smene |
| DELETE | `/api/shifts/{id}` | Manager/Owner, brisanje smene |

Primeri zahteva su u `Smena.API/SmenaAPI.http`.

## Mejlovi

Pozivnice se šalju preko SMTP-a (MailKit), pa radi sa bilo kojim servisom koji nudi SMTP (Resend, SendGrid, Gmail...). U produkciji lozinku zadaj kroz `Email__Password`. Lokalno Compose pokreće Mailpit, koji hvata sve mejlove umesto da ih šalje.

Slanje se obavlja unutar zahteva. Ako ne uspe, pozivnica je ipak sačuvana, a menadžer dobija link u odgovoru (`emailSent: false`) pa ga može poslati sam.

## Uloge

`Staff`, `Manager` i `Owner`. Uloga je dozvola u sistemu i nezavisna je od pozicije zaposlenog u smeni.
