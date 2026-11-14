# Etap 1 — dokumentacja terapii: kontrakt backendu

Wszystkie endpointy wymagają JWT z rolą `Therapist` i dotyczą wyłącznie własnych kart pacjentów zalogowanego terapeuty. Rola `Admin` nie przyznaje dostępu. Obcy identyfikator pacjenta, wizyty lub dokumentacji zwraca `404`; nie ujawniamy istnienia cudzego zasobu.

## Model i zasady

- Rozszerzamy istniejący `MedicalRecord`; dokumentacja jest powiązana z kartą pacjenta, wizytą i terapeutą. Backend sprawdza zgodność tych trzech powiązań. Nowy wpis zawsze wymaga wizyty.
- Jedna wizyta może mieć jedną dokumentację, również przy równoczesnym tworzeniu wpisów. Gwarantuje to unikalny indeks na `appointment_id`.
- Statusy JSON: `Draft` (szkic) i `Approved` (zatwierdzona).
- Szkic można wielokrotnie zapisywać. Historia przechowuje trwałe wersje zatwierdzone: pierwsze zatwierdzenie to wersja 1, każda korekta tworzy kolejną wersję. Zapisy szkiców nie tworzą wersji zatwierdzonych.
- Zatwierdzenie wymaga wizyty o statusie `Completed`. Dokumentowanie nie zmienia statusu wizyty. Korekta również sprawdza powiązania i status `Completed`.
- Zatwierdzonego wpisu nie można edytować przez endpoint szkicu. Korekta zapisuje pełny nowy stan oraz niezmienną wersję w jednej transakcji `SaveChanges`. Poprzednie wersje pozostają dostępne.
- API nie udostępnia usuwania dokumentacji ani jej wersji. EF odrzuca modyfikację i usuwanie zapisanych wersji; klucze obce blokują fizyczne usunięcie dokumentacji mającej historię.
- Archiwizacja karty pacjenta zachowuje wpisy i historię. Właściciel może nadal czytać dokumentację i wprowadzać korekty.
- Podsumowanie dla pacjenta jest osobnym polem. Ten etap nie publikuje go w portalu. Konto pacjenta nie otrzymuje żadnej treści dokumentacji, nawet gdy karta ma ustawioną dotychczasową flagę udostępniania.
- Istniejące notatki wizyty, wywiady JSON, HTML, mapy bólu i wiadomości rezerwacyjne pozostają w dotychczasowych polach. Nie kopiujemy ich do podsumowania ani do nowych pól. Nowe DTO nie serializują encji ani starych wewnętrznych notatek.
- Pola treści są zwykłym tekstem. Frontend powinien wyświetlać je jako tekst, bez interpretowania HTML.

## Pola treści

W JSON zapisu pola występują bezpośrednio w body; w odpowiedzi szczegółów i wersji znajdują się wewnątrz `content`.

- `interview`: wywiad, maksymalnie 10 000 znaków.
- `functionalAssessment`: obserwacje i ocena funkcjonalna, maksymalnie 10 000 znaków.
- `performedActions`: wykonane działania, maksymalnie 10 000 znaków.
- `recommendations`: zalecenia, maksymalnie 10 000 znaków.
- `patientSummary`: osobne podsumowanie przeznaczone dla pacjenta, maksymalnie 2 000 znaków; opcjonalne także przy zatwierdzaniu.

W szkicu każde pole treści jest opcjonalne. Pierwsze cztery pola muszą być niepuste i zawierać coś poza białymi znakami przy zatwierdzeniu oraz każdej korekcie. Początkowe i końcowe białe znaki są usuwane przy zapisie; pusty tekst staje się `null`. Limit długości sprawdzamy przed tym usunięciem.

`correctionReason` jest wymagany przy korekcie: od 1 do 1 000 znaków, z co najmniej jednym znakiem innym niż biały. Powód jest zapisywany po usunięciu skrajnych białych znaków. Nie jest publiczny.

## Endpointy

### Utworzenie szkicu

`POST /api/patients/{patientId}/medical-records`

```json
{
  "appointmentId": "<uuid-wizyty>",
  "interview": "Wywiad z wizyty",
  "functionalAssessment": null,
  "performedActions": null,
  "recommendations": null,
  "patientSummary": null
}
```

`appointmentId` musi być niepustym UUID własnej wizyty powiązanej z kartą wskazaną w URL. Backend ustala terapeutę z sesji. Status nowego wpisu to zawsze `Draft`. Odpowiedź: `201 Created`, nagłówek `Location` i pełne `MedicalRecordDto`.

### Zapis szkicu

`PUT /api/medical-records/{id}`

Body zawiera `version` i pola treści. To pełne zastąpienie treści szkicu: pominięte lub puste pola otrzymują `null`. Nie jest to PATCH. Nie można zmienić powiązań ani statusu.

```json
{
  "version": "<aktualny-token-uuid>",
  "interview": "Wywiad",
  "functionalAssessment": "Obserwacje i ocena",
  "performedActions": "Przeprowadzone działania",
  "recommendations": "Zalecenia",
  "patientSummary": "Osobne podsumowanie"
}
```

Odpowiedź: `200 OK`, pełne `MedicalRecordDto` z nowym tokenem `version`.

### Zatwierdzenie

`POST /api/medical-records/{id}/approve`

```json
{ "version": "<aktualny-token-uuid>" }
```

Zatwierdza treść już zapisanego szkicu. Wymaga `Completed` i kompletu czterech pól klinicznych. Odpowiedź: `200 OK`, `MedicalRecordDto`, status `Approved`, `currentVersion: 1`, nowy token `version`.

### Korekta zatwierdzonego wpisu

`POST /api/medical-records/{id}/corrections`

```json
{
  "version": "<aktualny-token-uuid>",
  "correctionReason": "Uzupełnienie oceny funkcjonalnej po sprawdzeniu pomiaru",
  "interview": "Pełna treść wywiadu",
  "functionalAssessment": "Pełna poprawiona ocena",
  "performedActions": "Pełna treść wykonanych działań",
  "recommendations": "Pełna treść zaleceń",
  "patientSummary": "Podsumowanie po korekcie"
}
```

Wymaga pełnej treści czterech pól klinicznych. Podsumowanie może być pominięte lub `null`; wtedy znika z bieżącej wersji, pozostając w poprzedniej. Odpowiedź: `200 OK`, pełne `MedicalRecordDto`, zwiększony `currentVersion` i nowy token `version`. Status pozostaje `Approved`.

### Szczegóły

`GET /api/medical-records/{id}` → `200 OK`

`MedicalRecordDto` zawiera:

- `id`, `patientId`, `appointmentId`, `therapistId`: UUID; `appointmentId` może być `null` tylko dla starszych wpisów.
- `status`: `Draft` albo `Approved`.
- `currentVersion`: numer ostatniej wersji zatwierdzonej; `0` dla szkicu.
- `version`: nieprzezroczysty token UUID współbieżności, wymagany przy każdej modyfikacji istniejącego wpisu.
- `createdAt`, `updatedAt`: czas UTC ISO 8601; `updatedAt` może być `null`.
- `content`: obiekt z pięcioma polami treści opisanymi powyżej, każde `string | null`.

`version` i `currentVersion` mają różne znaczenie. Frontend zawsze przesyła ostatni otrzymany token `version`; nie generuje go samodzielnie.

### Lista wpisów pacjenta

`GET /api/patients/{patientId}/medical-records?pageNumber=1&pageSize=20` → `200 OK`

Sortowanie: malejąco po terminie wizyty, następnie po ID. Dla starszych wpisów bez wizyty używany jest czas utworzenia. `pageNumber >= 1`, `pageSize` od 1 do 100; przesunięcie nie może przekroczyć `Int32.MaxValue`.

Odpowiedź `PaginatedList<MedicalRecordListItemDto>` ma `items`, `totalCount`, `pageNumber`, `pageSize`, `totalPages`, `hasPrevious`, `hasNext`.

Każdy element zawiera `id`, `appointmentId`, `appointmentStartsAt`, `status`, `currentVersion`, `createdAt`, `updatedAt`. Lista nie zawiera treści klinicznych. Pusta lista własnego pacjenta zwraca `200`; obca lub nieistniejąca karta zwraca `404`.

### Historia zatwierdzonych wersji

`GET /api/medical-records/{id}/versions` → `200 OK`

Tablica `MedicalRecordVersionDto`, rosnąco według numeru wersji:

```json
[
  {
    "versionNumber": 1,
    "authorUserId": "<uuid-konta-autora>",
    "recordedAt": "2026-11-14T12:00:00Z",
    "correctionReason": null,
    "content": {
      "interview": "Wywiad",
      "functionalAssessment": "Ocena",
      "performedActions": "Działania",
      "recommendations": "Zalecenia",
      "patientSummary": null
    }
  }
]
```

`authorUserId` wskazuje konto autora, nie ID profilu terapeuty. Autor i czas pochodzą wyłącznie z backendu. Pierwsza wersja ma `correctionReason: null`; kolejne mają wymagane uzasadnienie. Dla szkicu odpowiedź to `[]`.

Wszystkie odpowiedzi z tego kontrolera mają `Cache-Control: no-store`.

## Błędy i współbieżność

- `400`, `code: validation_failed`: brak wymaganego UUID/tokena, nieprawidłowy JSON, niedozwolona paginacja, brak wymaganej treści albo przekroczenie limitu. Błędy walidatora mają słowniki `errors` i `errorCodes`, z nazwami pól JSON (np. `interview`, `version`, `correctionReason`). Kody reguł: standardowe FluentValidation, np. `NotEmptyValidator`, `MaximumLengthValidator`. Błędy wiązania JSON mają standardowy format ASP.NET ProblemDetails z `code: validation_failed`.
- `401`: brak prawidłowej sesji.
- `403`: brak roli terapeuty; dotyczy także kont pacjentów i moderatorów opinii.
- `404`: brak własnego pacjenta, wpisu albo zgodnie powiązanej wizyty.
- `409`, `medical_record_already_exists`: dokumentacja dla tej wizyty już istnieje.
- `409`, `medical_record_correction_required`: próba nadpisania zatwierdzonego wpisu przez PUT.
- `409`, `medical_record_already_approved`: ponowna próba zatwierdzenia bieżącej wersji zatwierdzonego wpisu.
- `409`, `medical_record_not_approved`: próba korekty szkicu.
- `409`, `medical_record_completed_appointment_required`: wizyta nie ma statusu `Completed`.
- `409`, `concurrent_modification`: nieaktualny token lub rzeczywisty równoczesny zapis.

Przykład konfliktu:

```json
{
  "title": "Conflict",
  "status": 409,
  "code": "concurrent_modification",
  "detail": "Documentation changed. Reload it before saving."
}
```

Po konflikcie frontend zachowuje lokalnie edytowany tekst, pobiera aktualny wpis i umożliwia użytkownikowi rozstrzygnięcie zmian. Nie ponawia automatycznie zapisu z nowym tokenem. Jeden z równoczesnych zapisów wygrywa, drugi otrzymuje konflikt; przegrana operacja nie dodaje wersji historii.

## Migracja i weryfikacja

Migracja: `20261114124842_AddVisitDocumentationVersions`. Dodaje nowe pola i tabelę wersji bez usuwania istniejących danych. Starsze rekordy otrzymują `Draft`, `currentVersion = 0` oraz indywidualny token. Nie tworzymy fikcyjnych historycznych zatwierdzeń ani nie przenosimy starych treści do nowych pól. Starszy wpis bez wizyty można odczytać; zapis nowym API wymaga zgodnego powiązania z wizytą. Nie dodajemy endpointu przepinania takiego wpisu.

Zastosowanie migracji w docelowym środowisku:

```powershell
cd backend
dotnet ef database update --project PhysioFlow.Infrastructure --startup-project PhysioFlow.Api
```

Testy `MedicalRecordsTests`, `PostgresMedicalRecordsTests` i `MedicalRecordLoggingTests` obejmują przepływ dokumentowania, walidację, autoryzację, archiwizację, migrację danych istniejących, niezmienność wersji oraz równoczesne tworzenie, zapis szkicu, zatwierdzenie i korekty. Logowanie pipeline zapisuje nazwy operacji; komunikaty walidacji i konfliktów nie zawierają tekstów dokumentacji. W konfiguracji EF nie włączamy logowania wartości parametrów.

Pełna weryfikacja odpowiada poleceniom Backend CI. Testy PostgreSQL wymagają `PHYSIOFLOW_TEST_POSTGRES` wskazującego serwer testowy z prawem `CREATEDB`; fabryki tworzą osobne losowe bazy testowe i usuwają je po testach.

```powershell
dotnet restore PhysioFlow.sln
dotnet build PhysioFlow.sln --configuration Release --no-restore
dotnet test PhysioFlow.sln --configuration Release --no-build
```

Poza zakresem: integracje z zewnętrznymi systemami medycznymi, podpis elektroniczny, załączniki, eksport i publikowanie dokumentacji w portalu pacjenta.
