# Sprint 7, etap 1 — dziennik realizacji ćwiczeń pacjenta

## Zasady działania

Wpis dotyczy jednego wykonania albo jawnego pominięcia jednej pozycji **konkretnej, aktualnie udostępnionej wersji** planu. Statusy to `Completed` (w całości), `Partial` (częściowo) i `Skipped` (pominięte). Brak wpisu nie oznacza pominięcia. Status jest deklaracją pacjenta; backend nie wylicza go z liczby powtórzeń.

Uzgodnione zasady:

- Można zapisać wiele wykonań tej samej pozycji jednego dnia. Każde zamierzone wykonanie ma nowy `requestId`. Nie ma unikalności po dacie i ćwiczeniu.
- Autor może edytować i usuwać własne wpisy bez limitu czasu, również po cofnięciu publikacji, zakończeniu współpracy i ponownym powiązaniu karty.
- Historia należy do **konta autora** (`PatientUserId`), a nie do aktualnego konta przypiętego do karty. Pozostaje dostępna autorowi po odłączeniu, przepięciu na inne konto, ponownym powiązaniu i logicznym usunięciu planu, karty lub profilu terapeuty. Nowe konto przypięte do tej samej karty nie dziedziczy wpisów.
- Nowy wpis wymaga aktywnego, potwierdzonego konta z rolą `Patient`, istniejącego udostępnienia temu kontu, zgodnego okresu powiązania (`AccountLinkVersion`), nieusuniętych rodziców i zgodnego właściciela planu/karty. Przyjęto reguły dostępu z etapu udostępniania: samo zakończenie/archiwizacja planu lub archiwizacja karty zachowują istniejącą publikację. Zakończenie współpracy odłącza konto i blokuje nowe wpisy. Ponowne powiązanie wymaga ponownej publikacji.
- Cofnięcie publikacji lub publikacja innej wersji blokują **nowe** wpisy do poprzedniego udostępnienia. Nie usuwają historii.
- Obca i nieistniejąca pozycja, plan lub wpis zwracają `404`. Terapeuta i administrator nie otrzymują dodatkowego dostępu do tego API. Konto z dodatkową rolą moderatora podlega regułom pacjenta.

## Niezmienność historii

`exercise_journal_entries` przechowuje identyfikatory karty, konta autora, planu, niezmiennej `TherapyPlanVersion`, numer wersji, identyfikator pozycji w JSON tej wersji oraz czas publikacji i okres powiązania. Klucz obcy wskazuje niezmienną wersję, a nie obecną pozycję edytowalnego planu. Ta pozycja może później zniknąć z planu.

Przy dodawaniu backend kopiuje z opublikowanej wersji nazwę planu oraz opis ćwiczenia: nazwę, instrukcję, kolejność, dawkę, częstotliwość i uwagi pozycji. Nie przyjmuje tych tekstów od pacjenta. Odczyt dziennika używa zapisanej kopii. Zmiana biblioteki, usunięcie pozycji, korekta planu lub kolejna publikacja nie zmieniają wcześniejszych wpisów. Nie kopiujemy prywatnych uwag biblioteki, kluczy storage, podpisanych URL ani pełnej dokumentacji medycznej.

Edycja zmienia tylko dane raportowane przez pacjenta. Nie może przepiąć wpisu na inny plan, wersję lub pozycję. Nie tworzy osobnej historii rewizji treści komentarza; `createdAt` pozostaje niezmienne, a `updatedAt` wskazuje ostatnią edycję.

Usuwanie jest logiczne. Wpis znika ze wszystkich odczytów API, ale rekord i klucz ponowienia pozostają w bazie, żeby opóźniona retransmisja nie odtworzyła wpisu. Ten etap nie wprowadza automatycznego trwałego czyszczenia ani harmonogramu retencji. Zmiany planów, cofnięcie publikacji i zakończenie współpracy nie kasują danych dziennika.

## Endpointy

Wymagany JWT pacjenta. Odpowiedzi kontrolera mają `Cache-Control: no-store`. Wspólny prefiks: `/api/patient-portal/exercise-journal`.

- `POST /` — dodanie; `201 Created`, nagłówek `Location` oraz `ExerciseJournalEntryDto`. Powtórzenie wcześniej zatwierdzonego żądania daje `200 OK` z aktualnym stanem tego samego wpisu.
- `GET /` — własna lista: `PaginatedList<ExerciseJournalEntryDto>`.
- `GET /{id}` — własny, nieusunięty wpis: `200` i `ExerciseJournalEntryDto`.
- `PUT /{id}` — zastąpienie raportowanych danych własnego wpisu: `200` i DTO z nowym tokenem.
- `DELETE /{id}` — usunięcie z body `{ "version": "<token-wpisu>" }`: `204 No Content`. Kolejne usunięcie daje `404`.

### Dodanie — `CreateExerciseJournalRequest`

Przykład:

```json
{
  "requestId": "388a4c0b-48c2-4337-92be-8880e586795a",
  "therapyPlanId": "0d18d8ea-81b2-422f-9700-593a233d3e5b",
  "planVersionNumber": 1,
  "planItemId": "e1f5b20c-6958-4c31-b92d-041d9258bb4d",
  "performedOn": "2026-11-16",
  "status": "Partial",
  "actualSets": 2,
  "actualRepetitions": 8,
  "actualDurationSeconds": null,
  "painLevel": 0,
  "comment": "Przerwa po drugiej serii."
}
```

`planVersionNumber` pochodzi z `PatientTherapyPlanDto.versionNumber`, a `planItemId` z `content.exercises[].id` w `GET /api/patient-portal/therapy-plans/{id}`. To identyfikator pozycji, nie identyfikator ćwiczenia bibliotecznego. Nie wystarczy znać identyfikator planu lub jego nieudostępnionej wersji.

`requestId`, `therapyPlanId` i `planItemId` są niepustymi UUID; `planVersionNumber > 0`. `performedOn` i `status` są obowiązkowe. Pozostałe dane raportowane są opcjonalne:

- `actualSets`: 1–100.
- `actualRepetitions`: 1–1000.
- `actualDurationSeconds`: 1–86400.
- Nie wolno podać jednocześnie powtórzeń i czasu. Można podać same serie, pojedynczą miarę lub pominąć wszystkie miary; nie muszą odpowiadać dawce zaleconej. `Skipped` wymaga wszystkich trzech miar równych `null`/niepodanych.
- `painLevel`: liczba całkowita 0–10 lub `null`. `null`/brak oznacza brak oceny; `0` oznacza zgłoszony brak bólu. Ocena i komentarz są dozwolone również przy `Skipped`.
- `comment`: maksymalnie 2000 jednostek UTF-16 (`string.Length` w .NET). Spacje na krańcach są usuwane, a biały/pusty tekst zamieniany na `null`. Limit sprawdzamy przed przycięciem.

### Daty i strefa czasowa

`performedOn` jest datą kalendarzową `YYYY-MM-DD`, bez godziny i offsetu. Backend jej nie przesuwa. W tym etapie dzień raportowania jest interpretowany w `Europe/Warsaw`, z uwzględnieniem czasu letniego. Frontend powinien używać tej samej strefy do wyznaczenia „dzisiaj”; nie konwertować daty wykonania przez `new Date(...).toISOString()`.

Data wykonania musi być nie wcześniejsza niż `1900-01-01`, nie późniejsza niż bieżący dzień w Warszawie i nie wcześniejsza niż warszawski dzień danego udostępnienia. Obowiązuje to też przy zmianie daty istniejącego wpisu, z jego zachowanym `publishedAt`. Pozwala to na późniejsze uzupełnianie wpisów od dnia publikacji. Daty początku i końca planu są informacją terapeutyczną i nie nakładają dodatkowego ograniczenia daty wpisu.

`publishedAt`, `createdAt` i `updatedAt` są czasami UTC z `Z`. Wpis przechowuje datę wykonania oddzielnie od czasu utworzenia. Czasy tworzenia i edycji dziennika są zapisywane z precyzją mikrosekund zgodną z PostgreSQL.

### Wynik — `ExerciseJournalEntryDto`

```json
{
  "id": "039b8722-b489-4ea1-af18-d827024119cb",
  "patientId": "07572287-af21-415d-8e06-73caf69e0ea9",
  "therapyPlanId": "0d18d8ea-81b2-422f-9700-593a233d3e5b",
  "therapyPlanVersionId": "a9529c1f-a106-4b82-9d75-2cc4ab98dc60",
  "planVersionNumber": 1,
  "planItemId": "e1f5b20c-6958-4c31-b92d-041d9258bb4d",
  "publishedAt": "2026-11-16T09:00:00Z",
  "planName": "Plan ćwiczeń",
  "exercise": {
    "name": "Ćwiczenie oddechowe",
    "instructions": "Wykonuj spokojnie.",
    "order": 0,
    "sets": 3,
    "repetitions": 10,
    "durationSeconds": null,
    "frequency": "Codziennie",
    "notes": null
  },
  "performedOn": "2026-11-16",
  "status": "Partial",
  "actualSets": 2,
  "actualRepetitions": 8,
  "actualDurationSeconds": null,
  "painLevel": 0,
  "comment": "Przerwa po drugiej serii.",
  "createdAt": "2026-11-16T18:00:00Z",
  "updatedAt": null,
  "version": "d039f72e-d754-42eb-95b4-3bf6e66cf45c"
}
```

Teksty renderować jako zwykły tekst. `exercise` przedstawia zaleconą dawkę z historycznej wersji; `actual*` to deklarowana dawka wykonana. DTO nie zawiera identyfikatora konta autora, wewnętrznego hasha żądania ani okresu powiązania.

### Lista i filtry

Przykład: `GET /api/patient-portal/exercise-journal?pageNumber=1&pageSize=20&from=2026-11-01&to=2026-11-16&therapyPlanId=<uuid>&status=Partial`.

- `pageNumber`: od 1; domyślnie 1. `pageSize`: 1–100; domyślnie 20. Przepełnienie offsetu jest błędem `400`.
- `from` i `to`: opcjonalne, włączne granice **daty wykonania**; muszą być poprawnymi datami od `1900-01-01`, z `from <= to`. Przyszła granica filtra jest dozwolona.
- `therapyPlanId`: opcjonalny, niepusty UUID planu, obejmuje wszystkie jego zapisane wersje. Obcy/nieistniejący plan daje pustą listę.
- `status`: opcjonalny jeden z `Completed`, `Partial`, `Skipped`.

Filtry łączą się przez AND. Sortowanie: `performedOn DESC`, `createdAt DESC`, `id DESC` (stabilne przy remisach). Lista używa standardowego DTO: `items`, `totalCount`, `pageNumber`, `pageSize`, `totalPages`, `hasPrevious`, `hasNext`. Strona poza końcem daje `200` z pustym `items`. Paginacja offsetowa może przesuwać pozycje przy nowych wpisach lub zmianie dat między pobraniami.

### Edycja, usuwanie i konflikty

`UpdateExerciseJournalRequest` zawiera wyłącznie niepusty `version` oraz pola `performedOn`, `status`, `actualSets`, `actualRepetitions`, `actualDurationSeconds`, `painLevel`, `comment` jak przy dodawaniu. `PUT` zastępuje dane raportowane: pominięte opcjonalne pola są zerowane do `null`. Przykład:

```json
{
  "version": "d039f72e-d754-42eb-95b4-3bf6e66cf45c",
  "performedOn": "2026-11-16",
  "status": "Skipped",
  "painLevel": null,
  "comment": "Nie wykonałem ćwiczenia."
}
```

Token należy pobrać z ostatniego DTO wpisu. Po edycji trzeba zastąpić go nowym. Dwa zapisy lub zapis i usunięcie z tym samym tokenem mają tylko jednego zwycięzcę; drugi otrzymuje `409 concurrent_modification`. Frontend powinien pobrać bieżący wpis i pozwolić użytkownikowi rozstrzygnąć zmianę. Nie należy automatycznie nadpisywać nowszych danych.

Dodanie atomowo zapisuje wpis oraz zmienia tokeny współbieżności planu i karty. Dzięki temu równoczesne cofnięcie publikacji, podmiana wersji, zakończenie współpracy lub przepięcie konta nie pozwalają zapisać wpisu na nieaktualnym uprawnieniu. Operacja, która przegrywa wyścig, daje `409 concurrent_modification`. Zapis dziennika unieważnia dotychczasowy token edycji planu terapeuty; terapeuta musi odświeżyć plan przed kolejną zmianą. Równoczesne, niezależne wykonania na tej samej publikacji także mogą wymagać ponowienia po takim konflikcie.

### Ponowienia żądań

Frontend generuje losowy UUID `requestId` **raz dla zamierzonego nowego wpisu**, zachowuje go do uzyskania odpowiedzi i używa tego samego body przy ponowieniu po błędzie sieci lub `409 concurrent_modification`. W przypadku odrzucenia wersji/publikacji należy odświeżyć uprawnienia; do nowego wykonania lub świadomie zmienionego zgłoszenia użyć nowego klucza.

Unikalny indeks `(patient_user_id, request_id)` działa także na usuniętych rekordach. Hash pierwotnego, typowanego żądania z ujednoliconym komentarzem wykrywa użycie tego samego klucza z inną treścią (`409 idempotency_key_reused`). Nie zależy od kolejności pól JSON. Różnice wyłącznie w białych znakach na krańcach komentarza nie zmieniają jego tożsamości.

Identyczne powtórzenie daje `200` i **bieżący stan tego samego wpisu**; nie cofa późniejszej edycji. Działa również po odebraniu publikacji, ponieważ nie wykonuje nowego zapisu. Powtórzenie po usunięciu daje `409 journal_entry_deleted`, bez odtworzenia wpisu. Równoczesne identyczne żądania tworzą jeden rekord: zwycięzca dostaje `201`, powtórzenie `200`.

## Statusy i kody błędów

- `200` — odczyt, edycja lub potwierdzone ponowienie dodania.
- `201` — nowy wpis.
- `204` — usunięcie.
- `400 validation_failed` — brak obowiązkowych danych, niewłaściwe UUID/typy JSON, daty, status, zakresy, połączenie powtórzeń i czasu, dawka przy `Skipped`, długość komentarza lub paginacja. Walidacja aplikacyjna zwraca `errors` i `errorCodes`; błędy model binding używają standardowego `ValidationProblemDetails` z `code`.
- `401` — brak ważnego JWT; istniejący mechanizm JWT odrzuca też token dezaktywowanego/niepotwierdzonego konta.
- `403` — brak roli `Patient` lub odrzucone sprawdzenie aktywnego, potwierdzonego konta w handlerze.
- `404` — obcy, usunięty albo nieistniejący wpis, plan niedostępny w podanej wersji lub pozycja nieobecna w publikacji.
- `409 concurrent_modification` — stary token wpisu albo przegrany wyścig zapisu/uprawnienia.
- `409 idempotency_key_reused` — ten sam klucz dodania z inną treścią.
- `409 journal_entry_deleted` — próba ponowienia usuniętego wpisu.

Format błędów: istniejący `application/problem+json`, `title`, `status`, opcjonalnie `code`, `detail`, `errors`/`errorCodes`. Treści komentarzy i rzeczywiste zgłoszone wartości nie są częścią komunikatów walidacji ani logów. Pipeline loguje nazwy operacji, bez body/DTO. Pozostawić wyłączone EF `EnableSensitiveDataLogging`, HTTP body logging i logowanie obiektów żądania/odpowiedzi; nie dodawać komentarzy ani wartości bólu do telemetryki frontendu.

## Migracja i weryfikacja

Migracja `20261116205703_AddExerciseJournal` dodaje osobną tabelę, indeksy (autor/klucz, autor/data i autor/plan/data), klucze obce `RESTRICT` do karty i wersji oraz CHECK dla statusów, dawki, bólu i minimalnej daty. Nie zmienia dotychczasowego `patient_diary_entries`, planów, publikacji, biblioteki ani wersji. Nie przenosi starej dziennej listy wykonanych ćwiczeń: nie zawiera ona wiarygodnego powiązania z publikacją. Migrację stosować przed uruchomieniem nowego API.

```powershell
dotnet ef database update --project backend/PhysioFlow.Infrastructure --startup-project backend/PhysioFlow.Api
dotnet test backend/PhysioFlow.sln --configuration Release --filter FullyQualifiedName~ExerciseJournal
```

Testy obejmują własność konta i IDOR, role, potwierdzenie konta, jawne publikacje, niezmienność historii, ból `null`/`0`, walidację, wielokrotne wykonania, filtry, paginację, edycję/usuwanie i ponowienia. Testy jednostkowe sprawdzają także północ i zmianę czasu w Warszawie oraz prywatność logów. `PostgresExerciseJournalTests` weryfikują pełny kontrakt na PostgreSQL, migrację z zachowaniem starych danych, skrypt idempotentny, zgodność snapshotu modelu, CHECK i rzeczywiste wyścigi dodania, powtórzeń, edycji, usunięcia, cofnięcia publikacji i zakończenia współpracy.

Testy PostgreSQL wymagają `PHYSIOFLOW_TEST_POSTGRES` wskazującego lokalny serwer testowy z uprawnieniem `CREATEDB`; bez tej zmiennej są pomijane. Każda fabryka tworzy i usuwa wyłącznie własną losową bazę. Backend CI ustawia tę zmienną.
