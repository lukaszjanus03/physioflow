# Sprint 5 — wizyty pacjenta i przypomnienia

Backend korzysta z istniejącego kalendarza terapeuty. Frontend kalendarza pacjenta wymaga podłączenia do poniższego kontraktu. Dokument stanowi propozycję kontraktu do uzgodnienia z frontendem; nie oznacza, że integracja interfejsu została już wykonana.

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
  "durationMinutes": 60
}
```

Nie przesyłamy `patientId`, statusu, powodu ani notatek. Kartę wybiera backend na podstawie konta z sesji i aktywnej zaakceptowanej współpracy. Nieznane pola JSON nie dają uprawnień do zmiany innych danych.

Wizyta pacjenta ma pola: `id`, `therapist` (`id`, `firstName`, `lastName`, `practiceName`, `city`, `phoneNumber`), `startsAt`, `endsAt`, `durationMinutes`, `status`, `canCancel`, `cancellationDeadline`. Nie zawiera karty pacjenta, notatek ani powodu wpisanego przez terapeutę. Statusy są stringami: `Scheduled`, `Cancelled`, `Completed`, `NoShow`.

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
- `409` z `code: "concurrent_modification"` — równoczesna zmiana danych w pozostałych operacjach.

## Podsumowanie terapeuty

`GET /api/appointments/summary`, JWT i rola `Therapist`. Zwraca `{"upcomingAppointments":[…]}` — maksymalnie 5 własnych przyszłych wizyt `Scheduled`, od najbliższej. Każda ma istniejący format `AppointmentDto`; brak wizyt: pusta tablica. Endpointy kalendarza terapeuty nadal działają.

## Własność i historia

`appointments.patient_user_id` utrwala właściciela portalu. Jest ustawiany także przy tworzeniu wizyty przez terapeutę. Zakończenie współpracy odłącza konto od karty, ale pozostawia własne wizyty widoczne i możliwe do odwołania zgodnie z regułą 24 godzin. Dostęp do historii nie wymaga aktualnie aktywnej współpracy ani udostępnionej dokumentacji medycznej.

Pierwsze potwierdzone powiązanie karty przypisuje również wcześniejsze nieprzypisane wizyty. Ponowne połączenie karty z innym kontem nie przenosi własności istniejących wizyt. Przy zmianie karty wizyty przez terapeutę właściciel jest aktualizowany do konta nowej karty.

Migracja `AddPatientAppointmentsAndReminders` uzupełnia istniejące rekordy na podstawie historii zaakceptowanych/zakończonych współprac i aktualnych jednoznacznych powiązań. Niejednoznaczna historia pozostaje nieprzypisana; nie przyznajemy dostępu przez zgadywanie właściciela.

## Przypomnienia

`AppointmentReminderWorker` cyklicznie szuka przyszłych zaplanowanych wizyt, do których zostało najwyżej 24 godziny. Wizyta utworzona/przeniesiona mniej niż 24 godziny wcześniej otrzymuje jedno przypomnienie przy najbliższym przebiegu, o ile nadal jest przyszła. Zadanie nie odtwarza wiadomości po rozpoczęciu wizyty.

Rejestr `appointment_reminders` przechowuje wersję terminu, stan, liczbę prób, kolejny termin próby i czas wysłania. Unikalność `(appointment_id, schedule_id)` i blokada wizyty w PostgreSQL chronią równoległe zadania. Bezpośrednio przed wysyłką sprawdzane są bieżący status, termin i wersja wizyty. Blokada jest utrzymywana na czas wywołania dostawcy; równoczesna edycja/odwołanie może zaczekać na zakończenie wysyłki lub otrzymać `409` wymagające odświeżenia.

Zmiana terminu lub karty nadaje nowy `reminder_schedule_id` i czyści `ReminderSent`. Stare wpisy zostają jako historia i nie są ponawiane. Odwołanie usuwa wizytę z kolejki kandydatów. Błąd poczty nie jest częścią żądania rezerwacji lub odwołania i nie cofa tych operacji. Brak adresu e-mail oznacza `Skipped`, bez zatrzymania pozostałych wiadomości.

Adres przypomnienia dla wizyty konta portalu pochodzi z tego konta, również po zakończeniu współpracy. Nie jest zastępowany adresem nowego właściciela tej samej karty. Dla wizyt bez konta używany jest e-mail z karty. Treść zawiera termin warszawski z offsetem, imię i nazwisko terapeuty, nazwę gabinetu, miasto i telefon z istniejącego profilu. Model profilu nie ma jeszcze pola z adresem ulicznym gabinetu. Wiadomość nie zawiera imienia pacjenta, diagnoz, PESEL-u, powodu ani notatek.

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
