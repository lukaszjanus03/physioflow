# Sprint 9 — opcjonalne 2FA (TOTP)

Backend pacjenta i terapeuty. 2FA jest domyślnie wyłączone. Nie obejmuje SMS,
kodów przez e-mail ani zewnętrznych dostawców. Istniejący kontrakt tokenów pozostaje
zgodny dla kont bez 2FA, z dodatkowym polem `shouldPromptTwoFactor`.

## Logowanie

`POST /api/auth/login` — `{ "email": "…", "password": "…" }`.

Bez 2FA: `200` i zwykła sesja:

```json
{
  "accessToken": "JWT",
  "refreshToken": "…",
  "expiresAt": "2026-11-19T18:00:00Z",
  "shouldPromptTwoFactor": true
}
```

Z włączonym 2FA: poprawne hasło daje `200` i **wyłącznie**:

```json
{
  "requiresTwoFactor": true,
  "challengeToken": "…",
  "expiresAt": "2026-11-19T17:05:00Z"
}
```

Wyzwanie nie jest JWT i nie autoryzuje żadnych chronionych endpointów. Jest ważne
300 sekund, jednokrotne, związane z kontem i jego `SecurityStamp`. Nowe logowanie
hasłem unieważnia poprzednie wyzwanie tego konta. Hasło nie resetuje limitu prób 2FA.

`POST /api/auth/2fa/verify` jest anonimowy. Wymaga `challengeToken` oraz dokładnie
jednego z pól: `code` (sześć cyfr TOTP) lub `recoveryCode`:

```json
{ "challengeToken": "…", "code": "123456" }
```

```json
{ "challengeToken": "…", "recoveryCode": "32_ZNAKI_SZESNASTKOWE" }
```

Dopiero poprawny drugi krok zwraca `200` i sesję o strukturze opisanej powyżej.
Brak kodu, dwa rodzaje kodu jednocześnie, błędny kod, użyty kod odzyskiwania,
wygasłe lub zużyte wyzwanie nie wydają sesji. Błędny kod nie zużywa prawidłowego
wyzwania aż do wyczerpania limitu prób. Wyłączone konto i pacjent bez potwierdzenia
e-maila nie mogą zalogować się przez drugi krok.

`POST /api/auth/refresh` pozostaje bez zmian (`{ "refreshToken": "…" }`), zwraca
też `shouldPromptTwoFactor`. Refresh token pochodzi z ukończonej sesji, więc nie
wymaga ponownego TOTP przy każdej rotacji. Puste, wygasłe i unieważnione tokeny
są odrzucane. Wylogowanie nadal usuwa refresh token.

## Zarządzanie 2FA

Wszystkie poniższe operacje wymagają `Authorization: Bearer <accessToken>` i roli
`Patient` lub `Therapist`. Operacje dotyczą wyłącznie konta z JWT. Odpowiedzi auth
mają `Cache-Control: no-store`. Klient nie powinien zapisywać sekretu, URI QR,
wyzwania ani kodów odzyskiwania w telemetrii czy pamięci trwałej przeglądarki.

- `GET /api/auth/2fa/status` → `200`:

  ```json
  {
    "enabled": false,
    "setupPending": false,
    "recoveryCodesRemaining": 0,
    "shouldPromptTwoFactor": true
  }
  ```

- `POST /api/auth/2fa/setup` (bez body) → `200`:

  ```json
  {
    "sharedKey": "KLUCZ_BASE32",
    "authenticatorUri": "otpauth://totp/…?secret=…&issuer=PhysioFlow&algorithm=SHA1&digits=6&period=30",
    "expiresAt": "2026-11-19T17:10:00Z"
  }
  ```

  Frontend może wygenerować QR z `authenticatorUri` i wyświetlić `sharedKey` do
  ręcznego wpisania w aplikacji uwierzytelniającej. Nowa konfiguracja zastępuje
  poprzednią oczekującą. Nie zmienia logowania ani decyzji o propozycji.
  Zaczynanie konfiguracji przy już włączonym 2FA zwraca `409`.

- `POST /api/auth/2fa/confirm` — `{ "code": "123456" }` → `200`:

  ```json
  { "recoveryCodes": ["…", "…"] }
  ```

  Włącza 2FA dopiero po poprawnej weryfikacji sekretu oczekującego, ważnego przez
  600 sekund. Zapisuje ukończenie propozycji i wydaje 10 jednorazowych kodów.
  Ten zestaw jest wyświetlany tylko w tej odpowiedzi: użytkownik powinien go
  zachować w bezpiecznym miejscu. Przerwanie konfiguracji lub jej wygaśnięcie
  pozostawia logowanie hasłem. Potwierdzenie unieważnia wcześniejsze sesje;
  frontend powinien przejść do logowania z 2FA.

- `POST /api/auth/2fa/prompt/dismiss` (bez body) → `204`. Zapisuje na koncie
  pominięcie propozycji, obowiązujące na wszystkich urządzeniach. Nie blokuje
  ręcznej konfiguracji w przyszłości.

- `POST /api/auth/2fa/recovery-codes/regenerate` — ponowne uwierzytelnienie:

  ```json
  { "password": "…", "code": "123456" }
  ```

  Alternatywnie `password` i `recoveryCode` zamiast `code`. `200` zwraca
  `{ "recoveryCodes": ["…"] }`. Zastępuje cały zestaw, unieważniając wszystkie
  poprzednie kody oraz bieżące sesje i wyzwania. Frontend zachowuje nowe kody
  do jednorazowego wyświetlenia i kieruje użytkownika do ponownego logowania.

- `POST /api/auth/2fa/disable` — identyczne body ponownego uwierzytelnienia →
  `204`. Sam JWT, samo hasło i sam kod nie wystarczą. Usuwa materiał TOTP,
  kody odzyskiwania oraz sesje i wyzwania. Kolejne logowanie działa hasłem;
  propozycja 2FA nie wraca. Ręczna ponowna konfiguracja pozostaje dostępna.

Zmiany bezpieczeństwa zwiększają `AuthVersion` konta, unieważniając JWT, i usuwają
refresh token. Starsze JWT bez `auth_version` są traktowane jako wersja 0.
Nie ma nowej sesji w odpowiedzi konfiguracji, regeneracji ani wyłączenia.
Po potwierdzeniu konfiguracji trzeba poczekać na kolejny kod TOTP (następny okres
30 sekund) lub użyć kodu odzyskiwania — użyty kod TOTP nie może być wykorzystany ponownie.

## Propozycja 2FA

`shouldPromptTwoFactor=true` oznacza: konto pacjenta/terapeuty ma potwierdzony
e-mail, 2FA jest wyłączone i propozycja nie została obsłużona. Pole jest dostępne
w odpowiedzi udanego logowania/odświeżenia oraz w statusie. Przed potwierdzeniem
e-maila propozycja nie jest zgłaszana. Nieudane logowanie nie zwraca tej informacji.
Frontend pokazuje propozycję po pierwszym udanym logowaniu i wywołuje `prompt/dismiss`
przy pominięciu. Bez takiej decyzji API nadal zgłasza propozycję przy następnych logowaniach.

Obecny system wymaga potwierdzenia e-maila tylko od pacjentów. Terapeuci nadal
mogą logować się i ręcznie włączać 2FA bez potwierdzenia; dla niepotwierdzonego
adresu `shouldPromptTwoFactor=false`. Ten sprint nie dodaje procesu potwierdzania
e-maila terapeuty. Pominięcie lub poprawne ukończenie konfiguracji jest trwałe,
także po wyłączeniu 2FA i na innym urządzeniu.

## Błędy i limity

Błędy 2FA mają `application/problem+json`, np.:

```json
{ "title": "Two-factor authentication failed", "status": 401, "code": "two_factor_invalid_code" }
```

- `400 validation_failed`: nieprawidłowy JSON, zbyt długie pole; standardowy
  `ValidationProblemDetails` ASP.NET z `code`.
- `400 two_factor_setup_expired`: brak aktualnej konfiguracji lub jej wygaśnięcie.
- `401 two_factor_invalid_code`: brak/błędny/zużyty TOTP lub kod odzyskiwania,
  albo oba rodzaje dowodu naraz.
- `401 two_factor_invalid_challenge`: nieznane, wygasłe, zastąpione, zużyte
  wyzwanie lub zmiana `SecurityStamp`.
- `401 two_factor_invalid_credentials`: nieprawidłowe ponowne hasło albo zablokowane konto.
- `401/403`: brak/odrzucony JWT lub niewłaściwa rola (standardowy middleware).
- `403 email_confirmation_required`: pacjent musi potwierdzić e-mail.
- `409 two_factor_already_enabled` / `two_factor_not_enabled`: operacja
  niezgodna z bieżącym stanem.
- `409 concurrent_modification`: równoległa zmiana konta; pobrać świeży stan,
  a przy logowaniu rozpocząć nowe wyzwanie. Żaden konflikt nie wydaje tokenów/kodów.
- `429 two_factor_locked`: 5 błędnych dowodów na koncie; blokada na 300 sekund.
  Dotyczy konfiguracji, logowania i ponownego uwierzytelnienia. Utworzenie nowego
  wyzwania ani ponowne podanie poprawnego hasła nie zeruje tego licznika.
- `429 rate_limit_exceeded`: limit na IP; `Retry-After` z middleware limitera.

Logowanie i refresh mają limit `RateLimiting:Authentication` (20 żądań/15 min/IP).
Endpointy 2FA współdzielą `RateLimiting:TwoFactor` (60/15 min/IP).
Standardowa blokada Identity nadal obejmuje błędne hasła. Za reverse proxy trzeba
skonfigurować zaufane forwarded headers zgodnie z istniejącą konfiguracją API.
Odpowiedzi nie zawierają wpisanych sekretów ani kodów błędnych dowodów.

## Przechowywanie i wdrożenie

`TwoFactor` w `appsettings.json` zawiera issuer, czasy ważności, liczbę prób,
czas blokady i liczbę kodów. Walidacja ustawień jest wykonywana przy starcie.
Sekret ma 160 bitów, kod odzyskiwania 128 bitów entropii. TOTP używa SHA-1,
6 cyfr, kroku 30 sekund, tolerancji ±1 krok i odrzuca już wykorzystane kroki.
Zegar serwera powinien być synchronizowany. Zgodność algorytmu sprawdzają wektory
z [RFC 6238](https://www.rfc-editor.org/rfc/rfc6238.html).

Chroniony stan konta jest szyfrowany przez ASP.NET Core Data Protection i związany
z identyfikatorem użytkownika. Kody odzyskiwania i wyzwania przechowywane są jako
SHA-256 wewnątrz chronionego stanu. Aktualizacja stanu i wydanie tokenów korzystają
z jednej aktualizacji Identity z `ConcurrencyStamp`, aby równoległe próby nie
zużyły dwukrotnie tego samego dowodu. Stan i blokady są wspólne dla instancji API.

**Klucze Data Protection muszą być trwałe i wspólne dla instancji.** Opcjonalne
`DataProtection:KeyRingPath` (lub `DataProtection__KeyRingPath`) wskazuje katalog;
przykład developerski używa ignorowanego `.local-data-protection-keys`. Ścieżka
względna jest względem katalogu uruchomienia procesu. W produkcji użyć trwałego
wolumenu/chronionego katalogu i absolutnej ścieżki, właściwych uprawnień oraz
ochrony kluczy w spoczynku, zgodnie z
[dokumentacją Microsoft](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview).
Instancje muszą mieć tę samą tożsamość aplikacji Data Protection (domyślnie content
root). Zachowujemy dotychczasową tożsamość, aby istniejące linki potwierdzenia
e-maila pozostały ważne. Nie usuwać starych kluczy potrzebnych do
odszyfrowania sekretów; ich utrata uniemożliwia weryfikację istniejącego 2FA.
Nie włączać logowania HTTP body ani `EnableSensitiveDataLogging` na endpointach auth.
Middleware loguje dla nich jedynie typ błędu i status, bez wyjątku i jego treści.
Diagnostyczne `ToString()` DTO zawierających sekrety są redagowane.

Migracja `20261119145231_AddOptionalTwoFactor` dodaje do `AspNetUsers`:
`protected_two_factor_state` (nullable text), `two_factor_prompt_handled` (bool),
`auth_version` (int). Wykorzystuje istniejące Identity `two_factor_enabled`.
Istniejące konta dostają `two_factor_prompt_handled=true`, nowe zaczynają od false.
Migracja nie włącza 2FA na żadnym koncie. Wdrożyć ją **przed** uruchomieniem nowego API:

```powershell
dotnet ef database update --project backend/PhysioFlow.Infrastructure --startup-project backend/PhysioFlow.Api
```

Rollback usuwa te dane; nie wykonywać go po włączeniu 2FA bez odrębnej procedury
odzyskania kont (Identity `two_factor_enabled` pozostaje istniejącą kolumną).

## Testy

Testy HTTP InMemory i jednostkowe nie wymagają aplikacji uwierzytelniającej ani
zewnętrznych usług. Kontrolowany `TimeProvider` umożliwia wygaśnięcia bez czekania;
provider testowy Data Protection jest efemeryczny wyłącznie w testach. Testy
potwierdzają rejestrację, e-mail pacjenta, obie role, przerwanie/ponowienie konfiguracji,
logowanie, pominięcie propozycji, odrzucanie błędnych dowodów, odzyskiwanie,
regenerację, wyłączenie, limity i poufność logów. Dane przykładowe/testowe nie
ustawiają stałego sekretu ani obejścia TOTP w produkcji.

```powershell
dotnet test backend/PhysioFlow.Tests --filter 'FullyQualifiedName~TwoFactor|FullyQualifiedName~TotpCode'
```

PostgreSQL wymaga roli z CREATEDB. Fabryka tworzy i usuwa wyłącznie własną losową
bazę testową; sprawdza rzeczywiste migracje, oba role i równoległe użycie dowodów:

```powershell
$env:PHYSIOFLOW_TEST_POSTGRES = 'Host=localhost;Port=5432;Database=postgres;Username=physioflow;Password=dev'
dotnet test backend/PhysioFlow.Tests --filter 'FullyQualifiedName~PostgresTwoFactorTests'
```
