# Sprint 6, etap 3 — udostępnianie planów i podsumowań pacjentowi

## Uzgodnione zasady dostępu

- Udostępnienie jest jawną operacją terapeuty. Wskazuje konkretny numer niezmiennej wersji aktywowanego planu albo zatwierdzonej dokumentacji. Aktywacja, zatwierdzenie i korekta nigdy nie publikują treści automatycznie. Korekta pozostawia pacjentowi poprzednio udostępnioną wersję do kolejnej publikacji lub cofnięcia.
- Publikować może wyłącznie terapeuta będący właścicielem planu/dokumentacji i karty. Potrzebne są: niearchiwalna karta, powiązane aktywne konto z rolą `Patient` i potwierdzonym adresem e-mail oraz aktywna współpraca tego konta z terapeutą, wskazująca tę kartę.
- Plan musi mieć status `Active` lub `Completed`; `Draft` i `Archived` blokują nowe publikacje. Numer może wskazywać dowolną istniejącą aktywowaną wersję tego planu, również starszą niż bieżąca. Dokumentacja musi mieć status `Approved`, a jej własna, zgodnie powiązana wizyta — `Completed`. Podsumowanie wybranej zatwierdzonej wersji nie może być puste.
- Zakończenie planu, archiwizacja planu i archiwizacja karty **zachowują wcześniej udostępnione treści**. Nie ma automatycznej daty wygaśnięcia. Cofnięcie udostępnienia jest możliwe również w archiwum i po zakończeniu współpracy.
- Dostęp po zakończeniu współpracy wymagałby niezmienionego powiązania konta z kartą. Obecna operacja zakończenia współpracy odłącza konto od karty, więc **odbiera dostęp**. Ta istniejąca reguła pozostaje zachowana.
- Udostępnienie wiąże się z kontem i konkretnym okresem jego powiązania z kartą. Przepięcie na inne konto, odłączenie i ponowne podłączenie tego samego konta nie przekazują ani nie przywracają starych udostępnień. Potrzebna jest nowa publikacja. Rotacja `AccountLinkVersion` działa w EF i przez trigger PostgreSQL dla zmian `user_id` wykonywanych SQL-em.
- Usunięcie logiczne karty, planu, dokumentacji lub profilu terapeuty blokuje odczyt. Dezaktywowane lub niepotwierdzone konto pacjenta nie uzyskuje dostępu. Sama rola `Admin` nie przyznaje dostępu do treści medycznych; konto z dodatkową rolą moderatora podlega zwykłym zasadom pacjenta.
- Dotychczasowe `DocumentationSharedWithAccount` i `TherapyPlan.IsPublished` nie uprawniają do nowych odczytów pacjenta. Migracja nie publikuje istniejących danych. Publikacja nie ustawia szerokiej zgody na dokumentację karty.

## Endpointy terapeuty

Wymagany JWT z rolą `Therapist`. Obcy zasób i zasób nieistniejący dają `404`.

- `POST /api/therapy-plans/{id}/publish` — publikacja wersji planu.
- `POST /api/therapy-plans/{id}/revoke` — cofnięcie dostępu do planu.
- `POST /api/medical-records/{id}/patient-summary/publish` — publikacja podsumowania wersji dokumentacji.
- `POST /api/medical-records/{id}/patient-summary/revoke` — cofnięcie dostępu do podsumowania.

Publikacja przyjmuje `PublishPatientContentRequest`:

```json
{
  "version": "<bieżący-token-edycji-planu-lub-dokumentacji>",
  "versionNumber": 2
}
```

Cofnięcie przyjmuje `{ "version": "<bieżący-token-edycji>" }`. Nie wskazuje numeru treści: usuwa aktualne udostępnienie tego zasobu.

Odpowiedź `200` to odpowiednio dotychczasowy `TherapyPlanDto` lub `MedicalRecordDto`, rozszerzony o `publication`. Pole występuje też na elementach dotychczasowych list terapeuty (`TherapyPlanListItemDto`, `MedicalRecordListItemDto`):

```json
{
  "currentVersion": 3,
  "version": "<nowy-token-edycji>",
  "publication": {
    "versionNumber": 2,
    "patientUserId": "<konto-odbiorcy-publikacji>",
    "publishedAt": "2026-11-15T15:00:00Z",
    "isAccessible": true
  }
}
```

`publication = null` oznacza brak publikacji lub jawnie cofnięty dostęp. Po odłączeniu/przepięciu karty informacje o ostatniej publikacji pozostają dostępne terapeucie z `isAccessible = false`. `isAccessible` opisuje zgodność istniejącego powiązania konta z kartą; aktywność i potwierdzenie konta są dodatkowo sprawdzane przy rzeczywistym odczycie. Rozróżniaj `currentVersion` (najnowsza treść terapeuty) od `publication.versionNumber` (wybrana treść pacjenta).

Publikacja i cofnięcie zmieniają token `version` rodzica. Ten sam token chroni zapisy treści, korekty, przejścia statusów i operacje publikowania. Frontend zawsze zastępuje token wartością z odpowiedzi. Po `409 concurrent_modification` należy ponownie pobrać szczegóły i pozwolić terapeucie ponownie podjąć decyzję. Nie ponawiać publikacji automatycznie ze świeżym tokenem.

Publikacja zmienia też token karty w tej samej transakcji. Równoległe odłączenie konta, zakończenie współpracy, archiwizacja karty lub inna publikacja tej karty mogą zwrócić `409 concurrent_modification`. Przegrana transakcja nie zapisuje częściowego udostępnienia. Cofnięcie nie wymaga aktywnej współpracy ani aktywnego statusu planu/karty. Powtórne cofnięcie z aktualnym tokenem jest dozwolone; stary token zawsze daje konflikt.

## Endpointy pacjenta

Wymagany JWT z rolą `Patient` i aktywne, potwierdzone konto. Tożsamość wynika z sesji. Klient nie przekazuje identyfikatora konta ani karty.

- `GET /api/patient-portal/therapy-plans?pageNumber=1&pageSize=20`
- `GET /api/patient-portal/therapy-plans/{id}`
- `GET /api/patient-portal/therapy-plans/{id}/exercises/{itemId}/video/playback`
- `GET /api/patient-portal/visit-summaries?pageNumber=1&pageSize=20`
- `GET /api/patient-portal/visit-summaries/{id}`

`id` planu to identyfikator planu terapeuty. `id` podsumowania to identyfikator dokumentacji, nie wizyty. `itemId` to identyfikator pozycji **udostępnionej wersji planu**, nie identyfikator ćwiczenia w bibliotece ani nagrania.

Listy zwracają `PaginatedList<T>`: `items`, `totalCount`, `pageNumber`, `pageSize`, `totalPages`, `hasPrevious`, `hasNext`. Strony od 1, rozmiar 1–100, domyślnie 20. Niepoprawne parametry lub przepełniający offset dają `400`. Sortowanie: `publishedAt DESC, id DESC`. Ponowna publikacja może zmienić kolejność. Brak własnych udostępnień daje `200` i pustą listę.

Każdy odczyt szczegółów i wydanie linku sprawdzają bieżące powiązanie konta i niecofnięte udostępnienie. Obcy, nieopublikowany, cofnięty i nieistniejący zasób dają `404`. Pacjent nie otrzymuje historii wersji ani endpointów zapisu/publikowania; próby użycia endpointów terapeuty kończą się `403`. Wszystkie nowe odpowiedzi mają `Cache-Control: no-store`. Po wylogowaniu lub błędzie dostępu frontend usuwa lokalny stan treści i odtwarzacza.

## DTO pacjenta

`PatientTherapyPlanListItemDto`: `id`, `therapistId`, `name`, `versionNumber`, `startDate`, `endDate`, `publishedAt`. Nazwa i daty pochodzą z opublikowanej wersji, także po późniejszych korektach.

`PatientTherapyPlanDto`:

```json
{
  "id": "<uuid-planu>",
  "therapistId": "<uuid-profilu-terapeuty>",
  "versionNumber": 1,
  "publishedAt": "2026-11-15T15:00:00Z",
  "content": {
    "name": "Ćwiczenia domowe",
    "goal": "Poprawa ruchomości",
    "startDate": "2026-11-15",
    "endDate": "2026-12-15",
    "generalRecommendations": "Ćwicz bez bólu",
    "exercises": [
      {
        "id": "<uuid-pozycji-z-opublikowanej-wersji>",
        "order": 0,
        "name": "Ćwiczenie oddechowe",
        "instructions": "Instrukcja wykonania",
        "sets": 3,
        "repetitions": 10,
        "durationSeconds": null,
        "frequency": "Dwa razy dziennie",
        "notes": "Indywidualna wskazówka dla pacjenta",
        "video": { "source": "Upload", "youtubeUrl": null }
      }
    ]
  }
}
```

`PatientTherapyPlanItemDto` zawiera wyłącznie kopię nazwy i instrukcji oraz parametry i indywidualne wskazówki pozycji. `goal`, `generalRecommendations` i `exercises[].notes` są treścią przeznaczoną dla pacjenta. Terapeuta nie powinien umieszczać w nich wewnętrznych notatek. Prywatne `Exercise.Notes`, kategorie, identyfikatory bibliotecznych ćwiczeń/nagrań, klucze storage i historia korekt nie są zwracane. `video` może być `null`; `source` ma wartości `Upload` i `YouTube`. Dla `YouTube` zwracany jest kanoniczny `youtubeUrl` zapisany w wersji.

`PatientVisitSummaryListItemDto`: `id`, `therapistId`, `appointmentId`, `appointmentStartsAt`, `versionNumber`, `publishedAt`.

`PatientVisitSummaryDto` zawiera te same pola oraz `summary` (niepusty tekst z wybranej zatwierdzonej wersji). Nie zawiera `content`, wywiadu, oceny funkcjonalnej, wykonanych działań, wewnętrznych zaleceń dokumentacji, HTML, formularzy JSON, załączników, notatek wizyty, wiadomości rezerwacyjnej ani powodów korekt. Metadane wizyty pochodzą z powiązanej wizyty; treść podsumowania jest niezmiennym snapshotem.

Wszystkie teksty renderować jako zwykły tekst, bez interpretowania HTML/Markdown.

## Bezpieczne odtwarzanie

Endpoint pacjenta nie przyjmuje `videoId`. Wyszukuje pozycję w opublikowanej wersji i dopiero z niej odczytuje identyfikator niezmiennego materiału. Zmiana, odłączenie lub archiwizacja ćwiczenia w bibliotece oraz korekta planu nie zmieniają odtwarzanego materiału wcześniejszej publikacji. Materiały istniejące wyłącznie w historii pozostają dostępne dla tej publikacji.

Dla `Upload` odpowiedź `200` to `ExerciseVideoPlaybackDto`: `{ "url": "<podpisany-prywatny-url>", "expiresAt": "<UTC>" }`. Ważność wynosi 5 minut. Klucze storage nie występują w DTO planu ani w błędach; adres odtwarzania jest ograniczonym czasowo uprawnieniem i może zawierać ścieżkę obiektu. Frontend nie powinien zapisywać ani logować tego adresu. Również po wywołaniu storage backend ponownie sprawdza dostęp i token planu, aby wykryć cofnięcie podczas generowania linku; równoległa zmiana planu może wymagać ponownego pobrania szczegółów.

Cofnięcie blokuje następne odczyty i nowe wydawanie linków. **Wcześniej wydany podpisany URL może działać do końca swojej ważności**, maksymalnie przez pozostałą część pięciu minut. Cofnięcie nie usuwa materiału i nie unieważnia linku w R2. Odczyt rozpoczęty przed cofnięciem może być już w trakcie odpowiedzi; nie istnieje zdalne kasowanie treści pobranej przez pacjenta. YouTube jest zewnętrznym publicznym materiałem — odebranie dostępu w aplikacji nie usuwa filmu ani wcześniej poznanego adresu z serwisu YouTube.

Dla `YouTube` należy użyć zwróconego kanonicznego URL; endpoint podpisywania zwraca `409 video_not_uploaded`. Próba użycia pozycji bez nagrania, dowolnego identyfikatora lub pozycji nieobecnej w publikacji daje `404`. Endpoint terapeuty `GET /api/exercise-videos/{videoId}/playback` nadal wymaga roli `Therapist` i własnego materiału.

## Statusy i błędy

- `200` — odczyt, publikacja albo cofnięcie powiodły się.
- `400 validation_failed` — brak/pusty UUID tokenu, `versionNumber <= 0`, niepoprawna paginacja; `errors` i `errorCodes` w istniejącym formacie. Niepoprawny typ JSON lub UUID w body również daje `400` przez walidację MVC.
- `401` — brak ważnego uwierzytelnienia.
- `403` — brak wymaganej roli; dla odczytów pacjenta także nieaktywne/niepotwierdzone konto. Zmiana roli moderatora nie poszerza dostępu.
- `404` — zasób nie istnieje, jest cudzy lub niedostępny; przy publikacji również brak wskazanej zatwierdzonej/aktywowanej wersji tego zasobu.
- `409 concurrent_modification` — nieaktualny token lub przegrany wyścig transakcji.
- `409 patient_archived` — próba nowej publikacji dla archiwalnej karty.
- `409 patient_account_required` — brak powiązanego, aktywnego, potwierdzonego konta pacjenta.
- `409 cooperation_not_active` — brak aktywnej współpracy z tym kontem i kartą.
- `409 plan_not_publishable` — plan w statusie `Draft` lub `Archived`.
- `409 medical_record_not_approved` — próba publikacji podsumowania szkicu.
- `409 medical_record_completed_appointment_required` — brak zgodnie powiązanej, zakończonej wizyty.
- `409 patient_summary_empty` — wybrana zatwierdzona wersja nie ma niepustego podsumowania.
- `409 video_not_uploaded` — żądanie podpisanego linku do materiału YouTube.
- `503 storage_unavailable` — dostawca nagrań nie może wydać linku; odpowiedź nie zawiera diagnostyki dostawcy ani danych uwierzytelniających.

Błędy używają istniejącego `application/problem+json` (`title`, `status`, opcjonalnie `code`, `detail`). Kolejność walidacji może sprawić, że nieaktualny token jest zgłoszony przed błędem statusu treści. `404` nie służy do rozpoznawania, czy cudzy zasób istnieje.

## Migracja, zgodność i weryfikacja

Migracja `20261115174003_AddPatientContentSharing` dodaje nullable pola publikacji, klucze obce do istniejących niezmiennych wersji, indeksy list odbiorcy, `patients.account_link_version` i trigger rotacji powiązania. Istniejące plany, ćwiczenia, materiały, dokumentacja, wersje i historyczne flagi pozostają zachowane. Brak backfillu udostępnień jest celowy: starych flag nie można traktować jako zgody na przekazanie konkretnych treści konkretnemu kontu.

Istniejące endpointy terapeuty zachowują ścieżki, treść i zasady pracy; rozszerzone DTO mają opcjonalne pole `publication`. W API pacjenta nie wystawiamy encji EF ani DTO terapeuty. Wdrażanie wymaga zastosowania migracji przed uruchomieniem nowej wersji API. Migracje w testach są uruchamiane wyłącznie na losowych, izolowanych bazach.

Testy `PatientSharingTests` sprawdzają dostęp do własnych treści, role, IDOR, jawne publikowanie/cofanie, puste podsumowania, niezmienność po korektach, historyczne nagrania, cofnięcie podczas podpisywania, archiwizację i ponowne powiązanie konta. `PostgresPatientSharingTests` uruchamiają ten sam kontrakt na PostgreSQL i sprawdzają migrację, ponowne wykonanie skryptu idempotentnego, zachowanie wcześniejszych danych, SQL-owe przepięcie konta oraz rzeczywiste wyścigi publikacji/cofnięcia/korekty/zakończenia współpracy/archiwizacji karty.

Uruchomienie jak w Backend CI:

```powershell
$env:PHYSIOFLOW_TEST_POSTGRES = 'Host=localhost;Port=5432;Database=postgres;Username=physioflow;Password=dev'
dotnet build backend/PhysioFlow.sln --configuration Release
dotnet test backend/PhysioFlow.sln --configuration Release --no-build
```

Konto bazy testowej wymaga `CREATEDB`. Testy PostgreSQL są pomijane bez zmiennej `PHYSIOFLOW_TEST_POSTGRES`; CI już ją ustawia. Nie obejmuje to dziennika ćwiczeń, czatu, PDF, analityki ani płatności.
