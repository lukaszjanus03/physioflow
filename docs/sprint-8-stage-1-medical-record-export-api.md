# Sprint 8, etap 1 — zatwierdzona dokumentacja wizyt do eksportu PDF

Backend udostępnia dane do rozszerzenia istniejącego eksportu karty pacjenta. PDF nadal generuje frontend. Endpoint nie tworzy plików ani nie publikuje dokumentacji w portalu pacjenta.

## Wykorzystanie istniejącego API

- Lista `GET /api/patients/{patientId}/medical-records` zawiera metadane szkiców i zatwierdzonych wpisów, bez treści. Sortuje malejąco i nie filtruje po datach wizyt.
- `GET /api/medical-records/{id}` zwraca bieżący stan wpisu, również szkic, bez daty zatwierdzenia.
- `GET /api/medical-records/{id}/versions` zwraca wszystkie niezmienne zatwierdzone wersje z treścią i datą `recordedAt`. Pozwala odczytać zatwierdzony stan, ale wymaga osobnego żądania dla każdego wpisu oraz dodatkowego powiązania z datą wizyty.

Zachowujemy te kontrakty. Jedyny nowy endpoint łączy istniejące dane w stronicowaną listę ostatnich zatwierdzonych wersji, z filtrem dat wizyt. Nie wymaga migracji bazy ani nowego mechanizmu zatwierdzania.

## Endpoint i dostęp

```http
GET /api/patients/{patientId}/medical-records/export?from=2026-10-01&to=2026-10-31&pageNumber=1&pageSize=100
Authorization: Bearer <JWT>
```

Wymagana rola `Therapist` oraz własna karta pacjenta. Backend ustala profil terapeuty z JWT. Rola `Admin` ani powiązane konto pacjenta nie przyznają dostępu. Flagi udostępnienia i opublikowanie podsumowania w portalu nie wpływają na eksport pełnej dokumentacji przez jej właściciela.

Archiwalna karta pozostaje dostępna dla właściciela tak samo jak dotychczasowy odczyt dokumentacji. Usunięta logicznie karta zwraca `404`, a usunięte logicznie wpisy są pomijane. Powiązana wizyta musi należeć do tej samej karty i terapeuty; wpisy z niespójnym albo usuniętym powiązaniem nie są eksportowane.

Odpowiedź: `200 OK`, `Content-Type: application/json`, `Cache-Control: no-store`. Brak dopasowań jest poprawnym wynikiem, również dla karty bez dokumentacji.

## Parametry

- `patientId`: niepusty UUID karty pacjenta w ścieżce.
- `from`: opcjonalna data `YYYY-MM-DD`, najwcześniej `1900-01-01`; brak oznacza brak dolnej granicy.
- `to`: opcjonalna data `YYYY-MM-DD`, najwcześniej `1900-01-01`; brak oznacza brak górnej granicy. Jeśli podano obie granice, `from <= to`.
- `pageNumber`: liczba całkowita od 1, domyślnie 1.
- `pageSize`: liczba całkowita od 1 do 100, domyślnie 20.

Przesunięcie `(pageNumber - 1) * pageSize` nie może przekroczyć `Int32.MaxValue`. Parametry nie przyjmują zakresów godzinowych. Nie ma limitu długości zakresu dat.

## Wybór wpisów, wersji i dat

1. Eksport obejmuje wyłącznie zatwierdzone wpisy wskazanej karty. Szkice są pomijane, również gdy mają kompletną treść. Jeden wpis daje jeden element, niezależnie od długości historii.
2. Backend wybiera niezmienną wersję o numerze `MedicalRecord.currentVersion`. Treść pochodzi z `MedicalRecordVersion`, a nie z edytowalnych pól wpisu, formularzy JSON, HTML, notatek wizyty ani wiadomości rezerwacyjnej. Nieudana lub niezatwierdzona zmiana nie nadpisuje treści eksportu.
3. Korekta jest zatwierdzana atomowo i tworzy kolejną wersję. Eksport zwraca ostatnią zatwierdzoną korektę, niezależnie od wersji podsumowania opublikowanej pacjentowi. `approvedAt` pochodzi z `recordedAt` wybranej wersji, więc po korekcie jest to czas zatwierdzenia korekty.
4. Filtr dotyczy **początku wizyty** (`Appointment.startsAt`) przeliczonego na datę w strefie `Europe/Warsaw`, zgodnie z kalendarzem aplikacji. Obie daty są włączne. Backend porównuje UTC z początkiem dnia `from` i początkiem dnia po `to` (granica wyłączna), uwzględniając zmianę czasu. Nie filtruje po utworzeniu, edycji ani zatwierdzeniu dokumentacji. Wizyta rozpoczynająca się przed zakresem i kończąca w zakresie nie jest uwzględniana.
5. Starszy wpis bez przypisanej wizyty może znaleźć się **wyłącznie w eksporcie bez obu filtrów dat** i tylko jeśli ma zatwierdzoną wersję. Jego `appointmentId` oraz `appointmentStartsAt` są `null`. Podanie choć jednej granicy dat wyklucza taki wpis. Nie zastępujemy brakującej daty wizyty datą utworzenia ani zatwierdzenia. Istniejące API tworzenia i zatwierdzania nadal wymaga wizyty; migracja starszych wpisów nie nadała im automatycznie statusu zatwierdzonego.

Sortowanie jest stałe i rosnące: najpierw wpisy z wizytą według `appointmentStartsAt`, następnie według UUID wpisu `id`. W eksporcie bez filtrów wpisy bez wizyty są na końcu, uporządkowane rosnąco według czasu utworzenia wpisu i UUID. Nie oznacza to przypisania im daty wizyty. Frontend powinien zachować kolejność otrzymanych elementów.

## Odpowiedź

```json
{
  "items": [
    {
      "id": "f1e147f0-4614-48de-a093-c95471bc1656",
      "appointmentId": "8d96c3a2-b08f-48ad-8030-005a658d8542",
      "appointmentStartsAt": "2026-10-20T08:00:00Z",
      "versionNumber": 2,
      "approvedAt": "2026-11-18T10:02:00Z",
      "content": {
        "interview": "Wywiad z wizyty",
        "functionalAssessment": "Ocena funkcjonalna po korekcie",
        "performedActions": "Wykonane działania",
        "recommendations": "Zalecenia",
        "patientSummary": null
      }
    }
  ],
  "totalCount": 1,
  "pageNumber": 1,
  "pageSize": 100,
  "totalPages": 1,
  "hasPrevious": false,
  "hasNext": false
}
```

`id` to identyfikator wpisu dokumentacji. `appointmentId` to UUID wizyty lub `null`. `appointmentStartsAt` i `approvedAt` są czasami UTC w ISO 8601; tylko data wizyty może być `null`. Datę wizyty w PDF należy prezentować w `Europe/Warsaw`. `versionNumber` jest numerem zatwierdzonej wersji od 1, a nie tokenem UUID do edycji.

`content` ma istniejący kształt `MedicalRecordContent`: cztery wymagane pola kliniczne (`interview`, `functionalAssessment`, `performedActions`, `recommendations`, maksymalnie po 10 000 znaków) i opcjonalne `patientSummary` (do 2 000 znaków lub `null`). To zwykły tekst, bez interpretowania HTML. Nie zwracamy historii wersji, przyczyn korekt, danych autora, publikacji ani tokenów edycji.

Pusta odpowiedź dla pierwszej strony zawiera `items: []`, `totalCount: 0`, `totalPages: 0`, `pageNumber: 1`, żądane `pageSize`, `hasPrevious: false` i `hasNext: false`. Strona poza końcem zbioru również zwraca `200` i `items: []`, zachowując liczbę dopasowanych wpisów i żądany numer strony.

## Pobranie całego zakresu przez frontend

Frontend pobiera strony kolejno od `pageNumber=1`, zachowując na każdym żądaniu te same `patientId`, `from`, `to` i `pageSize` (zalecane 100). Dokleja `items` w otrzymanej kolejności i zwiększa `pageNumber` o 1, dopóki `hasNext` jest `true`. Nie ogranicza eksportu do pierwszych 20/100 wpisów ani do strony aktualnie otwartej w interfejsie dokumentacji. Po dowolnym błędzie przerywa operację; generuje PDF dopiero po poprawnym pobraniu wszystkich stron.

Przykładowy algorytm integracyjny (funkcja `get` dodaje JWT i odrzuca odpowiedzi spoza 2xx):

```ts
const entries = [];
let pageNumber = 1;
while (true) {
  const params = new URLSearchParams({ pageNumber: String(pageNumber), pageSize: "100" });
  if (from) params.set("from", from);
  if (to) params.set("to", to);
  const page = await get(`/api/patients/${patientId}/medical-records/export?${params}`);
  entries.push(...page.items);
  if (!page.hasNext) break;
  pageNumber += 1;
}
// Dopiero teraz przekaż entries do istniejącego generatora PDF.
```

Stronicowanie korzysta z istniejącego `PaginatedList`, bez sesji eksportu i bez migawki obejmującej wiele żądań. Przy niezmienionym zbiorze stałe sortowanie zapewnia komplet bez duplikatów, również przy identycznych terminach wizyt. Równoczesne zatwierdzenie nowego wpisu, zmiana daty wizyty lub usunięcie może zmienić granice stron; korekta może zmienić wersję pomiędzy żądaniami. Jeśli podczas eksportu wykonywano takie operacje, należy ponowić pobieranie od pierwszej strony. `totalCount` i `hasNext` opisują bieżące odczyty, nie są tokenem migawki.

## Błędy

- `400`, `code: validation_failed`: odwrócony zakres dat, data przed `1900-01-01`, niepoprawna data lub liczba w query, niepusty wymóg UUID albo niedozwolona paginacja. Błędy FluentValidation zawierają `errors` i `errorCodes` z kluczami `patientId`, `from`, `to`, `pageNumber`, `pageSize`. Błędy wiązania query mają standardowy ASP.NET ProblemDetails z `errors` i `code: validation_failed`. Wartość w ścieżce, która nie spełnia ograniczenia routingu `:guid`, daje `404`.
- `401`: brak poprawnego JWT.
- `403`: brak roli `Therapist`, również dla kont pacjenta i administratora.
- `404`: karta nie istnieje, jest usunięta logicznie albo należy do innego terapeuty. Nie ujawniamy istnienia obcej karty.

Eksport jest odczytem; nie wprowadza nowych błędów `409` ani nie zmienia statusu dokumentacji lub wizyty.

## Prywatność i weryfikacja

Endpoint dziedziczy `Cache-Control: no-store` z kontrolera dokumentacji. Pipeline loguje wyłącznie nazwy operacji, bez treści żądania lub odpowiedzi. Zapytanie wybiera jawne DTO, bez serializacji encji. Nie dodajemy logowania treści ani logowania wartości parametrów EF.

Testy `MedicalRecordExportTests`, `PostgresMedicalRecordExportTests` i `MedicalRecordLoggingTests` sprawdzają ostatnią zatwierdzoną korektę, brak niezatwierdzonych treści i danych historycznych w wyniku, brak szkiców i cudzych danych, odczyt kart archiwalnych, soft delete, walidację, puste wyniki, wpisy bez wizyt, granice doby przy obu zmianach czasu oraz przejście wszystkich stron dla 105 wpisów z powtarzającymi się terminami. Testy PostgreSQL weryfikują wykonanie zapytań na docelowym silniku i wymagają `PHYSIOFLOW_TEST_POSTGRES` z prawem `CREATEDB`; tworzą i usuwają własne losowe bazy testowe.

```powershell
cd backend
dotnet restore PhysioFlow.sln
dotnet build PhysioFlow.sln --configuration Release --no-restore
dotnet test PhysioFlow.sln --configuration Release --no-build
```

Poza zakresem: generowanie PDF na serwerze, zmiany frontendu, portal pacjenta, czat, dziennik, plany terapii, mapy bólu i eksport zbiorczy.
