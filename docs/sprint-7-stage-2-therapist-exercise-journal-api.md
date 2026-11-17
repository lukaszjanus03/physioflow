# Sprint 7, etap 2 — podgląd dziennika pacjenta przez terapeutę

## Cel i zależność

API umożliwia uprawnionemu terapeucie odczyt zgłoszeń wykonania ćwiczeń pacjenta oraz podstawowego podsumowania. Korzysta z danych [dziennika pacjenta z etapu 1](sprint-7-stage-1-exercise-journal-api.md). Nie dodaje edycji, usuwania, komentarzy terapeuty, eksportów, alertów ani funkcji real-time.

Status jest deklaracją pacjenta: `Completed` — wykonanie pełne, `Partial` — częściowe, `Skipped` — jawnie zgłoszone pominięcie. Backend nie wylicza statusu z dawki wykonanej i nie formułuje wniosków medycznych.

## Dostęp i granice historii

Każdy odczyt wymaga ważnego JWT z rolą `Therapist`. Serwer ustala terapeutę na podstawie zalogowanego konta; klient nie przekazuje identyfikatora terapeuty ani konta autora.

Dostęp jest możliwy tylko wtedy, gdy jednocześnie:

- Profil terapeuty i karta pacjenta nie są logicznie usunięte.
- Karta należy do tego terapeuty i nie jest zarchiwizowana.
- Karta ma obecnie powiązane aktywne, potwierdzone konto z rolą `Patient`.
- Istnieje zaakceptowana, nadal aktywna współpraca (`Accepted`) dla tej samej karty, terapeuty i konta pacjenta.
- Wpis nie jest logicznie usunięty, wskazuje tę kartę, jego `PatientUserId` odpowiada obecnemu kontu karty, a `PublicationLinkVersion` odpowiada bieżącemu `AccountLinkVersion` karty.

Znajomość UUID karty, planu lub wpisu nie daje dostępu. Filtry planu i pozycji wyłącznie zawężają dozwolony zakres. Nie można użyć ich do odczytu wpisów innego pacjenta lub terapeuty.

### Zakończenie współpracy i ponowne powiązanie

Zakończenie współpracy, odłączenie konta, usunięcie karty lub profilu terapeuty odbiera podgląd listy, szczegółów i podsumowania. Żądania zwracają `404`, tak jak dla zasobu nieistniejącego lub należącego do innego terapeuty.

Ponowne powiązanie konta tworzy nowy okres powiązania. Terapeuta **nie odzyskuje dostępu do wpisów poprzedniego okresu**, nawet jeżeli ponownie powiązano to samo konto z tą samą kartą. Nowy okres nie obejmuje też wpisów, które poprzedni autor później edytował: edycja nie zmienia autora ani identyfikatora okresu wpisu. W nowym okresie lista i podsumowanie obejmują wyłącznie nowe wpisy zapisane dla tego powiązania; szczegóły starszego wpisu zwracają `404`.

Nowe konto przypięte do tej samej karty nie dziedziczy historii poprzedniego autora. Nie ma automatycznego przywracania, przenoszenia lub łączenia wcześniejszych historii między kontami, kartami i terapeutami. Samo ponowne opublikowanie starej wersji planu nie przywraca starszych wpisów terapeucie. Nowe zgłoszenia wymagają publikacji zgodnej z nowym okresem powiązania, zgodnie z etapem 1.

Autor zachowuje dostęp do własnej historii w API pacjenta na zasadach etapu 1, także po zakończeniu współpracy. Ograniczenie podglądu terapeuty nie usuwa danych ani praw autora do własnych wpisów.

### Archiwizacja i zmiany planu

Archiwizacja karty blokuje wszystkie trzy odczyty terapeuty (`404`). Przywrócenie karty pozwala ponownie zobaczyć wpisy bieżącego okresu, jeżeli nadal istnieje to samo powiązanie i aktywna współpraca. Jeżeli w międzyczasie zakończono współpracę lub zmieniono powiązanie, samo przywrócenie karty nie odzyskuje wcześniejszej historii.

Cofnięcie publikacji planu, publikacja innej wersji, zakończenie lub archiwizacja planu oraz logiczne usunięcie planu lub ćwiczenia bibliotecznego nie odbierają podglądu istniejących wpisów, o ile warunki dostępu do pacjenta i bieżącego okresu są nadal spełnione. Podgląd historii nie wymaga obecnej publikacji danej wersji.

## Endpointy

Wspólny prefiks: `/api/patients/{patientId}/exercise-journal`, gdzie `patientId` jest UUID karty pacjenta. Nagłówek: `Authorization: Bearer <jwt-terapeuty>`. Odpowiedzi odczytu kontrolera mają `Cache-Control: no-store`.

- `GET /` — `200 OK`, `PaginatedList<ExerciseJournalEntryDto>`.
- `GET /{id}` — `200 OK`, `ExerciseJournalEntryDto`; `id` jest UUID wpisu. Wpis musi należeć do wskazanej karty i do dozwolonej historii.
- `GET /summary` — `200 OK`, `ExerciseJournalSummaryDto`, dla całego przefiltrowanego zakresu.

To API udostępnia wyłącznie odczyt. `POST`, `PUT`, `PATCH` i `DELETE` na poprawnej ścieżce listy lub szczegółów nie mają operacji zapisu i zwracają `405 Method Not Allowed`. Próba użycia pacjenckich endpointów zapisu `/api/patient-portal/exercise-journal` z samą rolą terapeuty zwraca `403`. Pole `version` w odczytanym DTO nie nadaje terapeucie prawa edycji lub usuwania.

## Filtry i stronicowanie

Lista i podsumowanie przyjmują identyczne opcjonalne filtry, łączone przez AND:

- `from`: dolna, włącznie, granica `performedOn` w formacie `YYYY-MM-DD`.
- `to`: górna, włącznie, granica `performedOn` w formacie `YYYY-MM-DD`.
- `therapyPlanId`: niepusty UUID planu; obejmuje wpisy wszystkich jego wersji dostępnych w bieżącym okresie powiązania.
- `planItemId`: niepusty UUID pozycji planu zapisanej we wpisie, nie UUID ćwiczenia bibliotecznego. Można łączyć z `therapyPlanId`; filtr nie wybiera automatycznie innych pozycji tego samego ćwiczenia.
- `status`: `Completed`, `Partial` lub `Skipped`. Frontend powinien wysyłać nazwy, nie liczby.

Daty muszą być poprawne i nie wcześniejsze niż `1900-01-01`; przy obu granicach wymagane jest `from <= to`. Przyszłe granice filtrów są dozwolone. Niepoprawna data, UUID, pusty UUID lub nieznany status daje `400 validation_failed`. Poprawny, ale niedopasowany plan lub pozycja daje pustą listę i zerowe podsumowanie; nie ujawnia, czy wskazany zasób istnieje poza dostępną historią.

Tylko lista używa `pageNumber` i `pageSize`:

- `pageNumber`: od 1, domyślnie 1.
- `pageSize`: od 1 do 100, domyślnie 20.
- Offset `(pageNumber - 1) * pageSize` nie może przekraczać `Int32.MaxValue`; niepoprawna paginacja daje `400 validation_failed`.

Sortowanie jest stałe: `performedOn DESC`, następnie `createdAt DESC`, następnie `id DESC`. UUID rozstrzyga remis daty i czasu, aby powtarzane odczyty niezmienionych danych zwracały tę samą kolejność. Paginacja offsetowa nie zamraża danych: dodanie, edycja daty albo usunięcie wpisu między odczytami może przesunąć strony. Strona poza końcem zwraca `200` z pustym `items` oraz prawidłowym `totalCount`.

Przykładowa para żądań z tym samym zakresem:

```http
GET /api/patients/07572287-af21-415d-8e06-73caf69e0ea9/exercise-journal?from=2026-11-01&to=2026-11-17&therapyPlanId=0d18d8ea-81b2-422f-9700-593a233d3e5b&planItemId=e1f5b20c-6958-4c31-b92d-041d9258bb4d&status=Partial&pageNumber=1&pageSize=20
GET /api/patients/07572287-af21-415d-8e06-73caf69e0ea9/exercise-journal/summary?from=2026-11-01&to=2026-11-17&therapyPlanId=0d18d8ea-81b2-422f-9700-593a233d3e5b&planItemId=e1f5b20c-6958-4c31-b92d-041d9258bb4d&status=Partial
```

Podsumowanie nie ma parametrów stronicowania. Frontend powinien przekazywać do niego wyłącznie filtry. `pageNumber` i `pageSize` nie ograniczają agregacji.

## Szczegóły — `ExerciseJournalEntryDto`

DTO jest wspólne z etapem 1. Przykładowe `GET /api/patients/07572287-af21-415d-8e06-73caf69e0ea9/exercise-journal/039b8722-b489-4ea1-af18-d827024119cb`:

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

Znaczenie pól:

- `id` wskazuje wpis, `patientId` kartę, `therapyPlanId` plan, `therapyPlanVersionId` niezmienną wersję, `planVersionNumber` jej numer, a `planItemId` pozycję w zapisanej wersji.
- `publishedAt` to zachowany czas publikacji będącej podstawą zgłoszenia. `planName` jest historyczną, opcjonalną nazwą planu.
- `exercise` to `JournalExerciseSnapshot`: historyczna nazwa, instrukcja, kolejność, zalecane serie, powtórzenia lub czas w sekundach, częstotliwość i uwagi pozycji. `name` jest tekstem, `order` liczbą całkowitą; `instructions`, `frequency`, `notes` i miary dawki mogą być `null`.
- `performedOn`, `status`, `actualSets`, `actualRepetitions`, `actualDurationSeconds`, `painLevel` i `comment` są deklaracją pacjenta. Opcjonalne miary i komentarz mogą być `null`.
- `createdAt` jest czasem utworzenia, `updatedAt` czasem ostatniej edycji albo `null`, a `version` tokenem bieżącego stanu wpisu.

Odpowiedzi DTO zachowują pola z wartością `null`; przykładowo brak oceny jest zwracany jako `"painLevel": null`, a nie jako pominięte pole. Dotyczy to także `lastPerformedOn` w podsumowaniu.

W interfejsie należy oddzielać zaleconą dawkę `exercise.*` od zgłoszonej `actual*`. Brak zgłoszonej miary nie oznacza wykonania z dawką zaleconą. `painLevel: null` oznacza brak oceny; `painLevel: 0` jest świadomie zgłoszoną oceną 0. Nie podstawiać zera za `null` ani wyliczać bólu ze statusu wykonania.

Teksty wyświetlać jako zwykły tekst. DTO nie ujawnia konta autora, okresu powiązania, klucza ponowienia, hasha żądania, kluczy storage ani prywatnych uwag biblioteki. Historyczne nazwy, instrukcje i dawki pochodzą z kopii zapisanej we wpisie; nie należy ich zastępować bieżącą biblioteką lub treścią aktualnej wersji planu.

## Lista — `PaginatedList<ExerciseJournalEntryDto>`

Przykład dla jednego wpisu w przefiltrowanym zakresie:

```json
{
  "items": [
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
  ],
  "totalCount": 1,
  "pageNumber": 1,
  "pageSize": 20,
  "totalPages": 1,
  "hasPrevious": false,
  "hasNext": false
}
```

`totalCount` obejmuje wszystkie dostępne, nieusunięte wpisy zgodne z filtrami, przed stronicowaniem. `totalPages` jest zaokrągleniem `totalCount / pageSize` w górę i wynosi 0 dla pustego zakresu. `hasPrevious` oznacza `pageNumber > 1`, a `hasNext` — `pageNumber < totalPages`.

## Podsumowanie — `ExerciseJournalSummaryDto`

```json
{
  "completedCount": 3,
  "partialCount": 2,
  "skippedCount": 1,
  "lastPerformedOn": "2026-11-17"
}
```

Reguły liczenia:

- `completedCount`, `partialCount` i `skippedCount` są liczbami wpisów o odpowiednim statusie w całym dozwolonym, przefiltrowanym zakresie. Suma odpowiada `totalCount` listy z tymi samymi filtrami, przy niezmienionych danych.
- Każdy osobny wpis jest osobnym zgłoszeniem. Trzy wpisy `Completed` tej samej pozycji tego samego dnia liczą się jako 3 pełne wykonania. Nie deduplikujemy po dniu, planie ani pozycji. Ponowienie dodania z tym samym kluczem etapu 1 nadal tworzy tylko jeden wpis.
- `lastPerformedOn` jest najpóźniejszą **datą wykonania** spośród wpisów `Completed` lub `Partial` spełniających filtry. Nie jest czasem utworzenia ani edycji. `Skipped` nie jest wykonaniem i nie wpływa na tę datę. Przy filtrze `status=Skipped` data zawsze wynosi `null`.
- Edycja wpisu przez autora zmienia bieżące liczniki, datę ostatniego wykonania i przynależność do filtrów. Liczymy aktualny stan wpisu, nie poprzednie rewizje. Logiczną operację usunięcia uwzględniają lista, szczegóły i podsumowanie: wpis znika, a jego licznik przestaje być naliczany.
- Brak wpisu nie jest pominięciem. Bez wpisów wynik ma wszystkie liczniki równe 0 i datę `null`. Zakres zawierający wyłącznie pominięcia ma dodatni `skippedCount` i datę `null`.
- Nie wyliczamy procentu realizacji, ponieważ opisowa częstotliwość planu nie stanowi jednoznacznego harmonogramu oczekiwanych wykonań. Wynik nie zawiera oceny skuteczności, zaleceń ani automatycznej interpretacji medycznej.

Przykład pustego zakresu dla uprawnionego pacjenta:

```json
{
  "completedCount": 0,
  "partialCount": 0,
  "skippedCount": 0,
  "lastPerformedOn": null
}
```

Pusty zakres daje `200`; brak uprawnienia do pacjenta daje `404`, a nie zerowe podsumowanie. Lista i podsumowanie są osobnymi odczytami, bez wspólnego zamrożenia danych. Jeśli pacjent zmieni wpis pomiędzy nimi, frontend powinien odświeżyć oba wyniki.

## Daty i strefa Europe/Warsaw

`performedOn`, `from`, `to` i `lastPerformedOn` są datami `YYYY-MM-DD`, bez godziny i offsetu. Oznaczają dzień kalendarzowy w `Europe/Warsaw`, z uwzględnieniem czasu letniego. Serwer nie przelicza granic filtrów na dni UTC. Frontend powinien przechowywać je jako daty i nie przepuszczać przez `new Date(...).toISOString()`.

`publishedAt`, `createdAt` i niepusty `updatedAt` są znacznikami UTC z `Z`. Można je wyświetlić po przeliczeniu na `Europe/Warsaw`. Data późniejszego dodania lub edycji nie zastępuje daty wykonania w filtrach i podsumowaniu. Zasady tworzenia i edycji dat pozostają zgodne z etapem 1: od warszawskiego dnia publikacji do bieżącego dnia w Warszawie.

## Błędy i zachowanie frontendu

- `400 validation_failed` — niepoprawne filtry, daty, status lub paginacja; błędne typy parametrów obsługuje model binding. Niepoprawny UUID w segmencie ścieżki ograniczonym przez `:guid` nie dopasowuje trasy i daje `404`.
- `401` — brak ważnego JWT, konto zalogowanego użytkownika nie istnieje lub jest nieaktywne; mechanizm JWT odrzuca również wymagające potwierdzenia, niepotwierdzone konto pacjenta. Frontend stosuje wspólną obsługę sesji.
- `403` — ważna sesja bez roli `Therapist`; dodatkowa rola administracyjna lub moderatora nie przyznaje dostępu do obcych pacjentów.
- `404` — karta niedostępna, obca, zarchiwizowana, odłączona lub usunięta; brak aktywnej współpracy, nieuprawnione konto pacjenta, wpis obcy, nieistniejący, usunięty lub z innego okresu powiązania. API celowo nie rozróżnia tych przypadków dla klienta.
- `405` — próba zapisu na trasie przeznaczonej wyłącznie do odczytu.

Przykładowy format `404` z istniejącego middleware (`application/problem+json`; nazwa zasobu w `detail` zależy od miejsca odmowy):

```json
{
  "title": "Resource not found",
  "status": 404,
  "detail": "Entity \"ExerciseJournalEntry\" (039b8722-b489-4ea1-af18-d827024119cb) was not found."
}
```

Walidacja aplikacyjna zwraca `title`, `status`, `code: "validation_failed"`, `errors` oraz `errorCodes` pogrupowane według pól. Błędy model binding mają standardowy `ValidationProblemDetails` z dodatkowym `code: "validation_failed"`. Odpowiedzi `401`, `403` z autoryzacji i `405` z routingu mogą nie zawierać tego samego JSON; klient powinien przede wszystkim sprawdzać status HTTP. Nie parsować tekstu `detail` jako kodu biznesowego.

Po `404` należy usunąć z widoku i pamięci podręcznej dane podglądu tego pacjenta, zamknąć szczegóły i nie wyświetlać wcześniej pobranej historii jako nadal dostępnej. Nie próbować odtworzyć uprawnienia przez inny endpoint lub samą zmianę filtrów. Przy ponownym uzyskaniu dostępu pobrać listę i podsumowanie od nowa. Odczyty nie nadają terapeucie prawa do późniejszych zapisów.

## Prywatność i weryfikacja

Pipeline loguje nazwy operacji, bez body i zwracanych DTO. Nie dodawać do logów, wyjątków, telemetryki ani analityki komentarzy, ocen bólu, treści instrukcji i innych treści wpisów. Pozostawić wyłączone EF `EnableSensitiveDataLogging` oraz logowanie treści żądań i odpowiedzi HTTP. Na frontendzie nie wysyłać tych danych do telemetryki ani trwałej pamięci przeglądarki.

Etap korzysta z zapisanych pól dziennika i okresu powiązania z etapu 1; nie przenosi ani nie odtwarza danych wcześniejszego dziennego dziennika. Weryfikacja backendu:

```powershell
dotnet test backend/PhysioFlow.sln --configuration Release --filter FullyQualifiedName~ExerciseJournal
```

Testy PostgreSQL wymagają lokalnego serwera testowego wskazanego przez `PHYSIOFLOW_TEST_POSTGRES` i uprawnienia `CREATEDB`. Bez tej zmiennej są pomijane; fabryki tworzą i usuwają własne losowe bazy testowe.
