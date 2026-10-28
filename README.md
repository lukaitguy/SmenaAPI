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

Migracije primenjuje servis `migrate` (migration bundle), pa tek onda kreće API.

Zaustavljanje: `docker compose down` (dodaj `-v` da obrišeš i podatke baze).

### Lokalno

Potrebni su .NET 10 SDK i PostgreSQL. Podrazumevani konekcioni string je u `Smena.API/appsettings.Development.json`.

```bash
docker compose up -d db
dotnet run --project Smena.API
```

API će slušati na http://localhost:5287.

## Migracije

Nova migracija: `dotnet ef migrations add <Naziv> -p Smena.Infrastructure -s Smena.API -o Persistence/Migrations`.

Za produkciju se koristi migration bundle, samostalan izvršni fajl bez SDK-a, koji se pokreće kao zaseban korak deploy-a pre novog API-ja:

```bash
docker build --target migrate -t smena-migrate .
docker run --rm -e ConnectionStrings__Smena="<connection string>" smena-migrate
```

Konekcioni string mora kroz env varijablu, jer bundle prvo diže host aplikacije. Lokalno (`dotnet run`) može i `Database:MigrateOnStartup=true`, što je podrazumevano uključeno samo u `appsettings.Development.json`. U produkciji ostaje isključeno.

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
| `RateLimiting:Auth` / `RateLimiting:ForgotPassword` | `PermitLimit` i `WindowSeconds` po IP adresi (podrazumevano 10 zahteva u minutu, odnosno 3 u 15 minuta) |
| `DataProtection:Certificate:Path` ili `:Base64`, `:Password` | PFX sertifikat kojim se šifruju DataProtection ključevi u bazi (u Compose-u kroz `DATAPROTECTION_CERT_BASE64` i `DATAPROTECTION_CERT_PASSWORD`). Bez njega su ključevi nešifrovani, uz upozorenje u logu. |
| `DataProtection:PreviousCertificate:*` | Prethodni sertifikat, samo dok traje rotacija |
| `ForwardedHeaders:Enabled` | Čita pravu adresu klijenta iz `X-Forwarded-For`. Uključiti samo kad je API dostupan isključivo preko reverse proxy-ja. |

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
| GET | `/api/shifts/mine?from=&to=` | objavljene smene prijavljenog zaposlenog |
| POST | `/api/shifts` | Manager/Owner, nova smena |
| PUT | `/api/shifts/{id}` | Manager/Owner, izmena smene |
| DELETE | `/api/shifts/{id}` | Manager/Owner, brisanje smene |
| POST | `/api/shifts/publish` | Manager/Owner, objavljuje nacrte u periodu `{ from, to }` i obaveštava zaposlene |
| POST | `/api/shifts/recurring` | Manager/Owner, smene po obrascu (dani u nedelji, od-do sat, period) |
| DELETE | `/api/shifts/series/{seriesId}?from=` | Manager/Owner, briše buduće smene serije |
| POST | `/api/swaps` | zaposleni traži da svoju buduću smenu prepusti kolegi (`shiftId`, `targetEmployeeId`, `note`) |
| GET | `/api/swaps/mine?status=` | zahtevi u kojima učestvujem (kao podnosilac ili kolega) |
| GET | `/api/swaps?status=` | Manager/Owner, svi zahtevi u restoranu |
| POST | `/api/swaps/{id}/accept`, `/decline` | kolega kome je smena ponuđena |
| POST | `/api/swaps/{id}/cancel` | podnosilac, dok zahtev nije završen |
| POST | `/api/swaps/{id}/approve`, `/reject` | Manager/Owner; tek odobravanje prebacuje smenu na kolegu |

Primeri zahteva su u `Smena.API/SmenaAPI.http`.

## Šifrovanje ključeva

ASP.NET DataProtection ključevi (njima se potpisuju npr. tokeni za reset lozinke) čuvaju se u tabeli `data_protection_keys`. Da ne bi stajali u bazi u običnom tekstu, u produkciji ih šifruj sertifikatom:

```bash
openssl req -x509 -newkey rsa:2048 -nodes -keyout key.pem -out cert.pem -days 3650 -subj "/CN=smena-dataprotection"
openssl pkcs12 -export -out keys.pfx -inkey key.pem -in cert.pem -passout pass:<lozinka>
base64 -w0 keys.pfx   # vrednost za DataProtection__Certificate__Base64
```

Sertifikat i lozinku čuvaj u tajnama deploy okruženja (ne u repou) i nemoj ih gubiti: bez sertifikata se ključevi iz baze ne mogu pročitati, pa postojeći tokeni prestaju da važe.

- Uključivanje šifrovanja ne prekida ništa: stari nešifrovani ključevi ostaju čitljivi, a novi se šifruju (stari se menjaju kad isteknu, posle oko 90 dana).
- Rotacija: novi sertifikat stavi u `DataProtection:Certificate`, a stari u `DataProtection:PreviousCertificate`. Stari ukloni kad svi ključevi koje je šifrovao isteknu.

## Ograničenje broja zahteva

Prijava, registracija, reset lozinke i pozivnice (`preview`, `accept`) dele zajednički budžet po IP adresi, a `forgot-password` ima stroži sopstveni. Kad se prekorači, API vraća `429` sa `Retry-After` zaglavljem. Pored toga, jedna email adresa dobija najviše 3 mejla za reset lozinke na sat, bez obzira sa koliko adresa stižu zahtevi.

Brojači su u memoriji instance, pa sa više instanci API-ja kvota važi po instanci. Iza reverse proxy-ja uključi `ForwardedHeaders:Enabled`, inače svi klijenti dele adresu proxy-ja i istu kvotu.

## Mejlovi

Pozivnice se šalju preko SMTP-a (MailKit), pa radi sa bilo kojim servisom koji nudi SMTP (Resend, SendGrid, Gmail...). U produkciji lozinku zadaj kroz `Email__Password`. Lokalno Compose pokreće Mailpit, koji hvata sve mejlove umesto da ih šalje.

Slanje se obavlja unutar zahteva. Ako ne uspe, pozivnica je ipak sačuvana, a menadžer dobija link u odgovoru (`emailSent: false`) pa ga može poslati sam.

## Objavljivanje rasporeda

Svaka nova smena (pojedinačna ili iz serije) je **nacrt** dok je menadžer ne objavi: vidi je samo menadžer (`publishedAt` je `null`). `POST /api/shifts/publish` sa `{ "from": ..., "to": ... }` objavljuje sve nacrte koji počinju u tom periodu (najviše 62 dana, npr. jedna nedelja).

- Zaposleni vide samo objavljene smene (`/api/shifts/mine`) i samo za njih mogu da traže prepuštanje.
- Svaki zaposleni dobija jedan mejl sa brojem svojih novih smena. Ponovno objavljivanje istog perioda ne šalje ništa novo.
- Izmena već objavljene smene ostaje objavljena i vidi se odmah; zaposleni za to nisu posebno obavešteni.
- Smene koje su postojale pre uvođenja objavljivanja važe kao objavljene.

## Ponavljajuće smene

`POST /api/shifts/recurring` razvija obrazac u obične smene, sve sa istim `seriesId`:

```json
{
  "employeeId": "...", "positionId": "...",
  "fromDate": "2026-11-02", "untilDate": "2026-12-27",
  "daysOfWeek": ["Monday", "Wednesday", "Friday"],
  "startTime": "08:00:00", "endTime": "16:00:00",
  "note": "stalni raspored",
  "onConflict": "Fail"
}
```

- Sat je po lokalnom vremenu restorana, pa smena od 08:00 ostaje u 08:00 i kad se pomeri sat (u bazi je UTC). Kraj pre početka znači smena preko ponoći (22:00-06:00).
- Period je najviše 180 dana, a smena najviše 16 sati. Zaposleni mora biti aktivan i imati poziciju, kao kod pojedinačne smene.
- `onConflict: "Fail"` (podrazumevano) odbija celu seriju ako se bilo koji termin preklapa sa postojećom smenom (409, ništa se ne pravi). `"Skip"` preskače takve dane i vraća ih u `skipped`.
- Svaka smena serije je obična smena: može se menjati, prepustiti kolegi ili obrisati pojedinačno i ostaje u seriji.
- `DELETE /api/shifts/series/{seriesId}` briše smene serije od zadatog trenutka (podrazumevano od sada), a prošle ostaju kao istorija.

## Prepuštanje smena

Zaposleni A svoju buduću smenu nudi kolegi B. B prihvata ili odbija, a menadžer odobrava ili odbija prihvaćene zahteve. Smena menja vlasnika tek pri odobravanju.

- Kolega mora biti aktivan, imati poziciju smene i biti slobodan u tom terminu. To se proverava pri zahtevu, pri prihvatanju i ponovo pri odobravanju.
- Za jednu smenu može postojati samo jedan otvoren zahtev (`Pending` ili `Accepted`).
- Mejl dobija kolega kad stigne zahtev, a oba zaposlena kad menadžer odluči.
- Nije prava razmena (smena za smenu), samo prepuštanje.

## Uloge

`Staff`, `Manager` i `Owner`. Uloga je dozvola u sistemu i nezavisna je od pozicije zaposlenog u smeni.
