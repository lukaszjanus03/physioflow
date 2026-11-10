# Sprint 5 — wizyty pacjenta i przypomnienia

Backend i podstawowy frontend kalendarza pacjenta są już zaimplementowane. Poniżej opisano kontrakt API, w tym uzupełnienie backendu o wiadomość pacjenta i wykluczanie edytowanej wizyty. Obsługa tych dwóch nowych pól wymaga jeszcze podłączenia po stronie frontendu.

## Endpointy pacjenta

Wymagane `Authorization: Bearer <JWT>` i rola `Patient`. Prefiks: `/api/patient-portal/appointments`.

- `GET ?from=2026-11-01&to=2026-11-30` — własne wizyty, również odwołane i historyczne. Zakres obejmuje dni w Europe/Warsaw, obie daty są wymagane; maksymalna różnica dat to 366 dni. Wynik: tablica, brak wizyt: `[]`.
- `GET /{id}` — szczegóły własnej wizyty; cudza i nieistniejąca wizyta zwracają `404`.
- `GET /summary` — najbliższa przyszła zaplanowana wizyta. Brak: `{"nextAppointment":null}`.
- `GET /therapists/{therapistId}/settings` — dozwolone długości wizyt: `{"therapistId":"…","timeZone":"Europe/Warsaw","allowedDurations":[30,45,60]}`.
- `GET /therapists/{therapistId}/available-slots?date=2026-11-30&durationMinutes=60` — wolne terminy. Format taki sam jak w kalendarzu terapeuty: `{"date":"2026-11-30","durationMinutes":60,"slots":[{"startsAt":"…Z","endsAt":"…Z"}]}`. Uwzględnia godziny, wyjątki, zajętość i bufor.
- `POST` — rezerwacja, odpowiedź `201` i nagłówek `Location`.
- `POST /{id}/cancel` — odwołanie; odpowiedź `200` z aktualną wizytą.

Payload rezerwacji:

```json
{
  "therapistId": "00000000-0000-0000-0000-000000000001",
  "startsAt": "2026-11-30T09:00:00Z",
  "durationMinutes": 60,
  "patientMessage": "Pierwsza konsultacja dotycząca bólu barku."
}
```

Nie przesyłamy `patientId`, statusu, powodu ani notatek. Kartę wybiera backend na podstawie konta z sesji i aktywnej zaakceptowanej współpracy. Nieznane pola JSON nie dają uprawnień do zmiany innych danych.

`patientMessage` jest opcjonalne. Backend usuwa skrajne białe znaki, pustą lub składającą się wyłącznie z białych znaków wartość zapisuje jako `null` i sprawdza limit 500 znaków po przycięciu. Przekroczenie limitu zwraca `400` z `errors.patientMessage`, bez zapisania rezerwacji. Brak pola i jawne `null` zachowują dotychczasowe działanie.

Wizyta pacjenta ma pola: `id`, `therapist` (`id`, `firstName`, `lastName`, `practiceName`, `city`, `phoneNumber`), `startsAt`, `endsAt`, `durationMinutes`, `status`, `canCancel`, `cancellationDeadline`, `patientMessage`. Nie zawiera karty pacjenta, notatek ani powodu wpisanego przez terapeutę. Statusy są stringami: `Scheduled`, `Cancelled`, `Completed`, `NoShow`.

Oryginalna wiadomość jest osobna od `Reason` i `Notes`. Właściwy terapeuta otrzymuje `patientMessage` w istniejącym `AppointmentDto`, w tym w szczegółach wizyty. `PUT /api/appointments/{id}` nie przyjmuje wiadomości do edycji i nie nadpisuje jej przy zmianie terminu, długości, powodu ani notatek. Pacjent nie ma endpointu do późniejszej zmiany wiadomości. Jeśli terapeuta przeniesie wizytę na kartę innego konta, nowy pacjent nie otrzymuje wiadomości poprzedniego autora (`patientMessage: null`); terapeuta zachowuje jej oryginał. Widoczność w DTO pacjenta wymaga zgodności autora rezerwacji (`CreatedById`) z właścicielem wizyty (`PatientUserId`).

Daty wizyt i granica odwołania są zwracane w UTC. Rezerwacja wymaga jednoznacznego czasu z `Z` lub offsetem, np. `+01:00`. Frontend prezentuje je z `timeZone: "Europe/Warsaw"`. `canCancel` jest wskazówką dla interfejsu; o przyjęciu odwołania decyduje ponowne sprawdzenie na serwerze. Dokładnie 24 godziny przed rozpoczęciem odwołanie jest dozwolone.

## Błędy

- `400` — błędny payload, data bez strefy, niewłączona długość wizyty lub błędny zakres dat; szczegóły walidacji w `errors`.
- `401` — brak ważnej sesji.
- `403` — brak roli Patient; pacjent nie ma dostępu do operacji terapeuty `complete`/`no-show`.
- `404` — brak własnej wizyty.
- `409` z `code: "cooperation_not_active"` — brak aktywnej współpracy, nieprawidłowe powiązanie karty albo równoczesna zmiana współpracy/karty podczas rezerwacji.
- `409` z `code: "card_archived"` — karta w archiwum.
- `409` z `code: "appointment_in_past"` — rezerwacja w przeszłości.
- `409` z `code: "slot_unavailable"` — termin poza dostępnością z uwzględnieniem bufora.
- `409` z `code: "slot_conflict"` — kolizja lub termin zajęty równocześnie. Należy odświeżyć wolne terminy.
- `409` z `code: "cancellation_too_late"` — do wizyty pozostało mniej niż 24 godziny.
- `409` z `code: "invalid_status_transition"` — wizyta nie jest już zaplanowana albo zmieniła się podczas odwołania.
- `409` z `code: "appointment_not_editable"` — próba wykluczenia z dostępności własnej wizyty rozpoczętej lub o statusie innym niż `Scheduled`.
- `409` z `code: "concurrent_modification"` — równoczesna zmiana danych w pozostałych operacjach.

## Podsumowanie terapeuty

`GET /api/appointments/summary`, JWT i rola `Therapist`. Zwraca `{"upcomingAppointments":[…]}` — maksymalnie 5 własnych przyszłych wizyt `Scheduled`, od najbliższej. Każda ma istniejący format `AppointmentDto`; brak wizyt: pusta tablica. Endpointy kalendarza terapeuty nadal działają.

## Wolne terminy podczas edycji wizyty

`GET /api/therapists/me/calendar/available-slots?date=2026-11-30&durationMinutes=60&excludeAppointmentId=<id>` wymaga JWT terapeuty. Opcjonalny parametr `excludeAppointmentId` pomija zajętość i bufor wskazanej wizyty. Pozostałe wizyty nadal blokują kolizje. Można pytać o inną datę i długość, aby przenieść lub wydłużyć wizytę.

Wykluczana wizyta musi należeć do zalogowanego terapeuty, mieć status `Scheduled` i rozpoczynać się po aktualnym czasie serwera. Cudza, nieistniejąca lub usunięta wizyta zwraca `404`, bez ujawniania jej danych; własna wizyta niespełniająca warunków edycji zwraca `409 appointment_not_editable`. Niepoprawny format GUID zwraca `400`.

Bez parametru endpoint działa jak dotąd. Endpoint wolnych terminów portalu pacjenta nie obsługuje wykluczania. Parametr dotyczy tylko podglądu dostępności: zapis `PUT /api/appointments/{id}` nadal ponownie sprawdza aktualną dostępność i wszystkie pozostałe wizyty w transakcji. Podgląd nie rezerwuje terminu.

## Własność i historia

`appointments.patient_user_id` utrwala właściciela portalu. Jest ustawiany także przy tworzeniu wizyty przez terapeutę. Zakończenie współpracy odłącza konto od karty, ale pozostawia własne wizyty widoczne i możliwe do odwołania zgodnie z regułą 24 godzin. Dostęp do historii nie wymaga aktualnie aktywnej współpracy ani udostępnionej dokumentacji medycznej.

Pierwsze potwierdzone powiązanie karty przypisuje również wcześniejsze nieprzypisane wizyty. Ponowne połączenie karty z innym kontem nie przenosi własności istniejących wizyt. Przy zmianie karty wizyty przez terapeutę właściciel jest aktualizowany do konta nowej karty.

Migracja `AddPatientAppointmentsAndReminders` uzupełnia istniejące rekordy na podstawie historii zaakceptowanych/zakończonych współprac i aktualnych jednoznacznych powiązań. Niejednoznaczna historia pozostaje nieprzypisana; nie przyznajemy dostępu przez zgadywanie właściciela. Kolejna migracja `AddAppointmentPatientMessage` dodaje nullable `patient_message varchar(500)`; istniejące wizyty zachowują dane i mają pustą wiadomość.

## Przypomnienia

`AppointmentReminderWorker` cyklicznie szuka przyszłych zaplanowanych wizyt, do których zostało najwyżej 24 godziny. Wizyta utworzona/przeniesiona mniej niż 24 godziny wcześniej otrzymuje jedno przypomnienie przy najbliższym przebiegu, o ile nadal jest przyszła. Zadanie nie odtwarza wiadomości po rozpoczęciu wizyty.

Rejestr `appointment_reminders` przechowuje wersję terminu, stan, liczbę prób, kolejny termin próby i czas wysłania. Unikalność `(appointment_id, schedule_id)` i blokada wizyty w PostgreSQL chronią równoległe zadania. Bezpośrednio przed wysyłką sprawdzane są bieżący status, termin i wersja wizyty. Blokada jest utrzymywana na czas wywołania dostawcy; równoczesna edycja/odwołanie może zaczekać na zakończenie wysyłki lub otrzymać `409` wymagające odświeżenia.

Zmiana terminu lub karty nadaje nowy `reminder_schedule_id` i czyści `ReminderSent`. Stare wpisy zostają jako historia i nie są ponawiane. Odwołanie usuwa wizytę z kolejki kandydatów. Błąd poczty nie jest częścią żądania rezerwacji lub odwołania i nie cofa tych operacji. Brak adresu e-mail oznacza `Skipped`, bez zatrzymania pozostałych wiadomości.

Adres przypomnienia dla wizyty konta portalu pochodzi z tego konta, również po zakończeniu współpracy. Nie jest zastępowany adresem nowego właściciela tej samej karty. Dla wizyt bez konta używany jest e-mail z karty. Treść zawiera termin warszawski z offsetem, imię i nazwisko terapeuty, nazwę gabinetu, miasto i telefon z istniejącego profilu. Model profilu nie ma jeszcze pola z adresem ulicznym gabinetu. Wiadomość nie zawiera imienia pacjenta, diagnoz, PESEL-u, powodu ani notatek.

Pole `patientMessage` także nie trafia do przypomnień. Logowanie MediatR zapisuje nazwy operacji, a nie payload rezerwacji. Walidacja limitu wiadomości nie odsyła ani nie loguje jej treści.

Resend otrzymuje stały klucz `appointment-reminder/{appointmentId}/{scheduleId}` i zapisany w bazie, niezmienny payload. Chroni to także przed powtórką po awarii między przyjęciem wiadomości a zapisem wyniku w bazie. [Resend utrzymuje klucze przez 24 godziny](https://resend.com/changelog/idempotency-keys); backend kończy ponawianie po 23 godzinach od przygotowania wysyłki. Domyślnie są maksymalnie 3 próby, z odstępami 5 i 10 minut po błędach. Stan `Sent` oznacza przyjęcie przez dostawcę, nie potwierdzenie dostarczenia do skrzynki.

Konfiguracja:

```json
{
  "AppointmentReminders": {
    "Enabled": true,
    "PollIntervalSeconds": 60,
    "BatchSize": 100,
    "MaxAttempts": 3,
    "RetryDelayMinutes": 5
  }
}
```

W głównym `appsettings.json` zadanie jest wyłączone. Ustaw `AppointmentReminders__Enabled=true` w środowisku z działającą pocztą i po wykonaniu migracji. Limity konfiguracji są sprawdzane przy starcie. Logi zadania zawierają identyfikatory, numery prób i typy błędów, bez payloadów, adresów i kluczy API.

## Lokalny podgląd poczty

W `appsettings.Development.json` ustaw `Email:PickupDirectory` na absolutną ścieżkę do katalogu `.local-emails` w repozytorium. Przykładowy plik deweloperski ma ścieżkę względną `../../.local-emails`, odpowiednią dla katalogu roboczego `backend/PhysioFlow.Api`. Ścieżka względna jest rozwiązywana względem katalogu roboczego procesu; absolutna eliminuje tę zależność.

Gdy `PickupDirectory` jest ustawiony, wszystkie wiadomości są zapisywane jako lokalne pliki HTML — bez wywoływania Resend. Dotyczy to również linków potwierdzenia konta. Katalog jest ignorowany przez Git. Stała nazwa pliku przypomnienia zapobiega lokalnym duplikatom. Aby wysyłać prawdziwe wiadomości, usuń/wyczyść `Email:PickupDirectory`, skonfiguruj `Resend:ApiKey` i `Resend:FromAddress`.

Po włączeniu przypomnień zarezerwuj przyszłą wizytę na najbliższe 24 godziny i poczekaj do następnego przebiegu. Sprawdź plik HTML oraz rekord `appointment_reminders`. Odwołanie powinno zwolnić termin, a przeniesienie na kolejny dzień zaplanować nową wersję przypomnienia.

## Weryfikacja i migracja

Z katalogu repozytorium:

```powershell
dotnet ef database update --project backend/PhysioFlow.Infrastructure --startup-project backend/PhysioFlow.Api
dotnet test backend/PhysioFlow.sln
```

Pierwsza komenda aktualizuje skonfigurowaną bazę aplikacji — nie jest wykonywana automatycznie przy starcie API.

Testy PostgreSQL używają oddzielnych baz `physioflow_calendar_test_<guid>`, tworzonych i usuwanych przez testy. Nie czyszczą bazy aplikacji. Konto testowe wymaga uprawnienia `CREATEDB` i możliwości instalacji `btree_gist`, której używa wcześniejsza migracja kalendarza. Lokalne uruchomienie:

```powershell
$env:PHYSIOFLOW_TEST_POSTGRES = 'Host=localhost;Port=5432;Database=postgres;Username=physioflow;Password=dev'
dotnet test backend/PhysioFlow.sln
```

Bez zmiennej testy PostgreSQL są jawnie pomijane; pozostałe działają z InMemory. Backend CI uruchamia PostgreSQL 16 i ustawia zmienną, więc testy bazy są wykonywane w CI. Testy przypomnień korzystają z kontrolowanego zegara i atrap poczty. Klucze Data Protection w testach żyją wyłącznie w pamięci hosta testowego.

Weryfikacja lokalna 10.11.2026: `dotnet test backend/PhysioFlow.sln --configuration Release --no-restore --verbosity minimal` ze skonfigurowanym PostgreSQL — 180 testów zakończonych powodzeniem, 0 błędów, 0 pominiętych. Cztery testy PostgreSQL obejmują konkurencję dwóch pacjentów, konkurencję pacjenta i terapeuty, równoległe zadania przypomnień oraz migrację istniejących wizyt po zakończonej współpracy. `dotnet ef migrations has-pending-model-changes` potwierdziło zgodność modelu z migracją; `git diff --check` nie wykazało błędów. Zdalny workflow GitHub Actions nie był uruchamiany w tej sesji. Migracje wykonano na izolowanych bazach testowych; baza aplikacji nie była aktualizowana.

Do domknięcia integracji: uzgodnienie kontraktu z frontendem, sprawdzenie ekranów pacjenta i terapeuty w przeglądarce oraz zapisanie wyników w PR. Testy API nie zastępują weryfikacji interfejsu.

## Weryfikacja uzupełnienia Sprintu 5

Lokalna weryfikacja 10.11.2026: **196 testów zakończonych powodzeniem, 0 błędów i 0 pominiętych**, w konfiguracji Release z PostgreSQL 16. Użyto argumentów testowych Backend CI (`--logger trx` i `--collect:"XPlat Code Coverage"`); brak zainstalowanego collectora XPlat w istniejącym projekcie oznacza brak raportu pokrycia, ale nie zatrzymuje testów. Wynik testów znajduje się w ignorowanym przez Git `backend/PhysioFlow.Tests/TestResults/sprint5-completion.trx`. `has-pending-model-changes` potwierdza zgodność modelu z migracją.

Dodane testy obejmują brak pola, jawne `null`, białe znaki, przycięcie, limit 500/501 znaków, odczyt i izolację wiadomości, zachowanie wiadomości przy edycji oraz brak jej ujawnienia po zmianie konta właściciela. Testy dostępności obejmują usunięcie zajętości i bufora własnej wizyty, zmianę długości, dalsze blokowanie innych terminów, odmowę wykluczenia cudzej lub nieedytowalnej wizyty i ponowne sprawdzenie kolizji przy zapisie. Dotychczasowe testy równoczesnych rezerwacji na PostgreSQL i granicy odwołania 24 godzin nadal przechodzą.

`ReminderWorkerVerificationTests` uruchamia aplikację z rzeczywiście włączonym `AppointmentReminderWorker` (interwał 1 sekunda), izolowaną bazą PostgreSQL i lokalnym `LocalPickupEmailService`. Nie wywołuje procesora ręcznie. Po przygotowaniu kont testowych przełącza dostawcę poczty na lokalny zapis HTML. Sprawdzono:

- odbiorcę zgodnego z kontem właściciela i dokładny termin w Europe/Warsaw;
- jeden plik i trwałe `Sent` dla pierwotnego terminu;
- nowy wpis i plik po przeniesieniu wizyty;
- brak kolejnych plików podczas następnych przebiegów;
- brak przypomnienia dla odwołanej wizyty;
- brak wiadomości pacjenta i prywatnych notatek w HTML.

Kontrolowany błąd dostawcy, opóźnienia ponowień, limit prób, odwołanie przed ponowieniem i skuteczne ponowienie są sprawdzane osobnymi testami z atrapą poczty. `Sent` oznacza przyjęcie przez wybranego dostawcę: w lokalnym teście zapis pliku, a w Resend przyjęcie żądania przez API. **Nie jest to potwierdzenie dostarczenia do skrzynki odbiorcy.** W tej weryfikacji nie wysyłano rzeczywistych wiadomości przez Resend.

Do przekazania frontendowi:

1. `frontend/src/lib/patient-appointments-api.ts`: opcjonalne `patientMessage?: string | null` w payloadzie i pole w odpowiedzi; formularz `PatientBookingDialog` oraz szczegóły wizyty powinny je obsłużyć.
2. `frontend/src/lib/appointments-api.ts` i `AppointmentEditor`: podczas edycji przekazywać `excludeAppointmentId=appointment.id`, uwzględnić identyfikator w kluczu cache zapytania i korzystać z odpowiedzi backendu. Obecne ręczne dopisywanie oryginalnego terminu nie rozwiązuje zmiany długości wizyty i nie powinno omijać bieżącej dostępności.
3. Po podłączeniu sprawdzić wspólny przepływ rezerwacji z wiadomością, szczegóły u terapeuty, zmianę długości/terminu i odwołanie.

Pełnego przepływu w przeglądarce nie potwierdzono: narzędzie przeglądarki było niedostępne, a dostęp do okna Chrome wygasł. Przygotowano lokalny frontend i izolowany serwer API; zatrzymano je po próbie weryfikacji. Pozostaje wspólne sprawdzenie po podłączeniu nowych pól przez frontend. Migrację i testy wykonywano na bazach testowych; baza aplikacji nie była zmieniana.
