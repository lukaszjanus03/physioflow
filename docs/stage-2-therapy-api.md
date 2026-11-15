# Terapia — kontrakt backendu do integracji frontendowej

Opisane tutaj API terapeuty wymaga JWT z rolą `Therapist`. Właścicielem jest profil terapeuty ustalany z sesji. Klient nie przekazuje `therapistId`, `authorUserId`, statusu, numeru zatwierdzonej wersji ani treści kopii ćwiczeń. Obce ćwiczenia, plany, pozycje i karty zwracają 404. Sama rola `Admin` nie daje dostępu. Konta pacjentów nie mają dostępu do tych endpointów; aktywacja i powiązanie konta z kartą nie publikują planu. Jawne udostępnianie wybranej wersji i osobne endpointy pacjenta opisuje [etap 3](stage-3-patient-sharing-api.md). Karta bez konta jest obsługiwana.

Wszystkie pola tekstowe są zwykłym tekstem: API nie interpretuje HTML ani Markdown, zachowuje znaczniki jako tekst i usuwa jedynie skrajne białe znaki. Frontend renderuje je jako tekst, bez `innerHTML`. Puste opcjonalne teksty stają się `null`. Żądania i odpowiedzi nie są logowane; log operacji zawiera jej nazwę. Odpowiedzi biblioteki oraz odczyty planów i historii mają `Cache-Control: no-store`.

## Biblioteka ćwiczeń

- `GET /api/exercises?search=&category=&archived=false&pageNumber=1&pageSize=20` — lista.
- `POST /api/exercises` — tworzenie, 201 i `Location` do szczegółów.
- `GET /api/exercises/{id}` — szczegóły, także zarchiwizowanego ćwiczenia.
- `PUT /api/exercises/{id}` — pełne zastąpienie pól ćwiczenia z tokenem `version`, 200.
- `POST /api/exercises/{id}/archive` — archiwizacja, body `{ "version": "uuid" }`.
- `POST /api/exercises/{id}/restore` — przywrócenie, takie samo body.

Pola tworzenia: `name` (wymagane, 1–200 znaków), `instructions` (wymagane, 1–10000), `category` (wymagana kategoria lub obszar ciała, 1–100), `notes` (opcjonalne, do 2000). Białe znaki nie spełniają wymogu niepustej wartości. Edycja dodatkowo wymaga `version`. Można edytować własne ćwiczenie również w archiwum.

`ExerciseDto`: `id`, `name`, `instructions`, `category`, `notes`, `isArchived`, `version` (UUID), opcjonalne `video`. Obsługę materiałów i ich kopii w pozycjach planów oraz historii opisuje [kontrakt Sprintu 6](sprint-6-exercise-videos-api.md).

`search` (do 200 znaków) przeszukuje nazwę i instrukcję bez rozróżniania wielkości liter; `category` (do 100) jest dokładnym filtrem bez rozróżniania wielkości liter. `archived=false` domyślnie zwraca bieżące ćwiczenia, `true` tylko archiwalne, pusty parametr `archived=` oba rodzaje. Sortowanie: `name ASC, id ASC` przy kolacji bazy. Strony są numerowane od 1, rozmiar 1–100 (domyślnie 20); niepoprawny lub przepełniający offset daje 400. Stabilność dotyczy niezmienionego zbioru; zmiany pomiędzy żądaniami mogą przesunąć strony.

Listy zwracają istniejący w projekcie `PaginatedList<T>`: `items`, `totalCount`, `pageNumber`, `pageSize`, `totalPages`, `hasPrevious`, `hasNext`.

## Plany

- `GET /api/patients/{patientId}/therapy-plans?status=Draft&pageNumber=1&pageSize=20` — lista własnej karty. Opcjonalny filtr `status`; domyślnie wszystkie statusy. Sortowanie `createdAt DESC, id DESC`; paginacja jak w bibliotece.
- `POST /api/patients/{patientId}/therapy-plans` — tworzy szkic, 201 i `Location`.
- `GET /api/therapy-plans/{id}` — szczegóły bieżącego stanu, 200.
- `PUT /api/therapy-plans/{id}` — pełny zapis szkicu, 200.
- `POST /api/therapy-plans/{id}/activate` — aktywacja zapisanego szkicu, body `{ "version": "uuid" }`.
- `POST /api/therapy-plans/{id}/corrections` — pełna, od razu aktywowana korekta aktywnego planu; wymaga treści, tokenu i `correctionReason` (niepuste, do 1000 znaków).
- `POST /api/therapy-plans/{id}/complete` — zakończenie, body `{ "version": "uuid" }`.
- `POST /api/therapy-plans/{id}/archive` — archiwizacja, takie samo body.
- `GET /api/therapy-plans/{id}/versions` — historia aktywacji w kolejności numerów wersji, 200.

Nie ma endpointu usuwania planu ani historii. Aktywacja nie publikuje planu i nie ustawia udostępnienia pacjentowi. Wiele planów jednej karty może być aktywnych jednocześnie; nie ma automatycznych przejść zależnych od dat.

### Treść i limity

Tworzenie i zapis przyjmują `name` (do 200), `goal` (do 10000), `startDate`, `endDate` (daty `YYYY-MM-DD`), `generalRecommendations` (do 10000), `exercises` (tablica do 100 pozycji, domyślnie `[]`; `null` jest błędem). PUT i korekta dodatkowo wymagają `version`. PUT zastępuje całą treść, więc pominięcie pola usuwa jego poprzednią wartość.

Szkic może nie mieć nazwy, celu, dat ani pozycji. Jeśli obie daty są podane, koniec musi być nie wcześniejszy niż początek. Skrajne daty `0001-01-01` i `9999-12-31` są odrzucane. Aktywacja/korekta wymaga niepustej nazwy, obu poprawnych dat i co najmniej jednej kompletnej pozycji. Cel i zalecenia ogólne pozostają opcjonalne.

Pozycja żądania:

```json
{
  "id": null,
  "exerciseId": "uuid-własnego-ćwiczenia",
  "sets": 3,
  "repetitions": 10,
  "durationSeconds": null,
  "frequency": "2 razy dziennie",
  "notes": "Indywidualne wskazówki"
}
```

- `sets`: liczba serii, całkowita 1–100; wymagana dla kompletnej pozycji.
- `repetitions`: powtórzenia na serię, całkowite 1–1000.
- `durationSeconds`: czas jednej serii w sekundach, całkowity 1–86400.
- Używa się **dokładnie jednego** z `repetitions` i `durationSeconds` przy aktywacji/korekcie. W szkicu oba mogą być puste; równoczesne podanie obu zawsze daje 400. Podane liczby także w szkicu muszą mieścić się w zakresach.
- `frequency`: opis częstotliwości, do 200 znaków, wymagany dla kompletnej pozycji; bez automatycznej interpretacji harmonogramu.
- `notes`: opcjonalny tekst do 2000 znaków.

### Identyfikacja i aktualizacja pozycji

Nowa pozycja ma pominięte lub puste `id` (`null`) i wymagane `exerciseId`. Backend nadaje jej UUID oraz kopiuje nazwę i instrukcję z własnej biblioteki. Zarchiwizowane ćwiczenie daje 409 `exercise_archived`; cudze lub nieistniejące daje 404. Można dodać to samo ćwiczenie wielokrotnie jako różne pozycje.

Istniejąca pozycja przesyła otrzymane `id` i niezmienione `exerciseId`. UUID musi należeć do tego planu. Zmiana źródłowego ćwiczenia wymaga usunięcia pozycji i dodania nowej bez `id`. Powtórzony UUID w tablicy daje 400. Pominięcie pozycji usuwa ją z bieżącej treści; zatwierdzona historia nadal ją zawiera.

Kolejność tablicy jest wiążąca: backend nadaje `order` od 0, bez luk. Frontend przesuwa cały obiekt z jego `id` i parametrami. `order`, `name` i `instructions` nie są polami zapisu. Kopie istniejących pozycji nigdy nie są odświeżane z biblioteki; pozostają czytelne i mogą być używane w korektach nawet po archiwizacji ćwiczenia. Ponowne dodanie takiego ćwiczenia jako nowej pozycji jest zabronione.

### Odpowiedzi

`TherapyPlanDto`: `id`, `patientId`, `status`, `currentVersion`, `version`, `createdAt`, `updatedAt`, `content`.

`content`: `name`, `goal`, `startDate`, `endDate`, `generalRecommendations`, `exercises`. Każda pozycja zawiera `id`, `exerciseId`, `order`, kopie `name`, `instructions` oraz `sets`, `repetitions`, `durationSeconds`, `frequency`, `notes`. Data audytu jest w UTC. Szczegóły nie ujawniają danych konta, dokumentacji medycznej ani notatek ćwiczenia z biblioteki.

`TherapyPlanListItemDto`: `id`, `name`, `status`, `currentVersion`, daty okresu i audytu. Lista nie zawiera treści klinicznych ani pozycji.

Historia jest tablicą `TherapyPlanVersionDto`: `versionNumber`, `authorUserId` (wyłącznie identyfikator autora, bez danych konta), `activatedAt` (UTC), `correctionReason` (`null` przy pierwszej aktywacji), `content` (cała niezmienna treść i pozycje). Autor i czas pochodzą z serwera.

### Wersje i dozwolone przejścia

`currentVersion` / `versionNumber` to kolejne liczby aktywowanej historii: szkic ma 0, pierwsza aktywacja 1, każda poprawna korekta +1. Dowolna liczba zapisów szkicu nie tworzy historii. Zakończenie i archiwizacja nie tworzą nowych wersji treści.

`version` to niezależny, niepusty UUID kontroli współbieżności. Każdy zapis i zmiana statusu zwraca nowy token. Klient przesyła ostatnio odczytany token. Stary token i faktyczny wyścig zapisu zwracają 409 `concurrent_modification`; trzeba odczytać aktualny stan i rozstrzygnąć różnice. Nie należy automatycznie ponawiać zapisu ze świeżym tokenem bez oceny zmian. Nawet ponowiona archiwizacja/przywrócenie ćwiczenia wymaga aktualnego tokenu.

- `Draft → Draft`: PUT szkicu.
- `Draft → Active`: aktywacja kompletnego szkicu.
- `Active → Active`: kompletna korekta z uzasadnieniem i nową wersją historii.
- `Active → Completed`: zakończenie.
- `Draft / Active / Completed → Archived`: archiwizacja, bez utraty treści.
- `Completed` i `Archived` nie pozwalają na dalszą edycję ani ponowną aktywację; dla nowej terapii tworzy się nowy plan. Ponowna archiwizacja planu jest niedozwolona.

PUT aktywnego planu jest niedozwolony: należy użyć korekty. Zapis treści, pozycje, token i nowa aktywowana wersja są atomowe w jednej transakcji. Historia ma unikalny numer na plan, zakaz aktualizacji/usuwania w EF oraz trigger PostgreSQL; klucze obce chronią przed kaskadowym usunięciem historii.

### Archiwalna karta pacjenta

Archiwizacja karty zachowuje listę, szczegóły i wszystkie wersje planów. Każda mutacja planu archiwalnej karty (także utworzenie, aktywacja, korekta, zakończenie i archiwizacja planu) daje 409 `patient_archived`. Po przywróceniu karty stosowane są zwykłe reguły statusów. Biblioteka ćwiczeń pozostaje niezależna od archiwizacji kart.

### Błędy

- 400 `validation_failed`: brak wymaganych pól/tokenu, limity, parametry, zduplikowane pozycje, daty lub paginacja. Odpowiedź zawiera `errors` i `errorCodes` według pól, bez przesłanych tekstów. Niepoprawny JSON/typ/enum może być odrzucony standardowym MVC ProblemDetails przed handlerem.
- 401: brak uwierzytelnienia.
- 403: brak roli terapeuty.
- 404: brak własnego zasobu, cudzy zasób, obcy identyfikator pozycji lub próba podmiany `exerciseId` istniejącej pozycji.
- 409 `concurrent_modification`: nieaktualny token lub wyścig zapisów.
- 409 `exercise_archived`: próba dodania nowej pozycji z archiwum.
- 409 `invalid_plan_transition`: niedozwolony status operacji.
- 409 `patient_archived`: karta nie dopuszcza zapisów.

### Migracja i integracja

Migracja `20261114150019_AddTherapyLibraryAndPlanVersions` rozszerza istniejące tabele. Zachowuje stare opisy, multimedia, parametry, notatki, identyfikatory i flagi publikacji. Uzupełnia instrukcję z dawnego opisu, kategorię z `MuscleGroup`, kopie w dotychczasowych pozycjach oraz losowe tokeny. Porządkuje starsze pozycje według starego `order`, potem UUID. Nowe i dotychczasowe plany zaczynają z historią 0 w statusie `Draft`: nie tworzymy fikcyjnych aktywacji i autorów dla starszych danych. Starsze niekompletne pozycje wymagają poprawienia przed aktywacją. Stare treści przekraczające nowe limity pozostają zachowane; nowe zapisy podlegają limitom.

Frontend może integrować bibliotekę i edytor na podstawie DTO z `backend/PhysioFlow.Application/DTOs/Therapy.cs` oraz tego kontraktu. Po każdym zapisie należy zastąpić lokalny stan odpowiedzią serwera (w tym nowymi UUID pozycji i tokenem). Historia wyświetla oddzielny `content` wybranej aktywowanej wersji. Etap nie obejmuje portalu pacjenta, dziennika wykonań, mediów ani PDF.

Weryfikacja CI: `dotnet build PhysioFlow.sln --configuration Release`, następnie `dotnet test PhysioFlow.sln --configuration Release --no-build`. Ustawienie `PHYSIOFLOW_TEST_POSTGRES` z uprawnieniem CREATEDB uruchamia dodatkowo testy prawdziwych wyścigów, migracji i niezmienności historii; istniejący Backend CI już udostępnia PostgreSQL 16 i tę zmienną. Każda fabryka korzysta z osobnej, losowej bazy testowej.
