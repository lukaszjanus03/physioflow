# Sprint 5 — powiadomienia o wizytach (backend)

Proponowany kontrakt do uzgodnienia z frontendem przed integracją.
Wszystkie endpointy wymagają JWT i roli `Patient` lub `Therapist`.
Odbiorca zawsze pochodzi z sesji; API nie przyjmuje `recipientId`.
Daty są zwracane w UTC (ISO 8601 z `Z`).

## REST

### `GET /api/notifications?pageNumber=1&pageSize=10`

Lista własnych powiadomień, posortowana malejąco po `createdAt`, a przy tej samej
dacie po `id`. Domyślnie strona 1 i rozmiar 10; `pageNumber`: 1–1 000 000,
`pageSize`: 1–100. Nieprawidłowe wartości zwracają `400`. Strona poza zakresem
zwraca pustą listę z aktualnym `totalCount`.

Odpowiedź `200`:

```json
{
  "items": [
    {
      "id": "72ff5466-330e-4b46-aebe-a9940434ce01",
      "type": "AppointmentBooked",
      "title": "Nowa rezerwacja wizyty",
      "body": "Pacjent zarezerwował wizytę.",
      "appointmentId": "0d8a83fc-a709-41c5-a3cf-b9be43b6af12",
      "createdAt": "2026-11-11T12:00:00Z",
      "readAt": null
    }
  ],
  "totalCount": 1,
  "pageNumber": 1,
  "pageSize": 10,
  "totalPages": 1,
  "hasPrevious": false,
  "hasNext": false
}
```

Typy zdarzeń:

- `AppointmentBooked`: pacjent rezerwuje wizytę → konto właściwego terapeuty.
- `AppointmentCreated`: terapeuta tworzy wizytę → właściciel konta pacjenta,
  jeśli wizyta ma `PatientUserId`.
- `AppointmentRescheduled`: terapeuta zmienia początek lub koniec wizyty
  (również przez zmianę długości) → właściciel konta pacjenta.
- `AppointmentCancelled`: odwołanie przez którąkolwiek stronę → druga strona.

Zmiana samych notatek/powodu, ponowne zapisanie identycznego terminu oraz
oznaczenie wizyty jako odbytej/nieobecności nie generują powiadomienia.
Przeniesienie wizyty na inną kartę jest traktowane jako `AppointmentCreated`
dla właściciela docelowej wizyty. Autor nigdy nie otrzymuje własnego zdarzenia.

### `GET /api/notifications/unread-count`

Odpowiedź `200`: `{ "count": 3 }`. Liczone są wyłącznie własne rekordy
z `readAt == null`, niezależnie od strony listy i obecności połączenia SignalR.

### `PATCH /api/notifications/{id}/read`

Bez body. Odpowiedź `204`. Ustawia `readAt` tylko przy pierwszym odczycie.
Ponowne i równoległe żądania zachowują pierwszą datę odczytu.
Nieistniejący lub cudzy identyfikator zwraca `404` (bez ujawniania istnienia rekordu).

### `POST /api/notifications/read-all`

Bez body. Odpowiedź `204`. Oznacza wszystkie aktualnie nieprzeczytane własne
rekordy jako przeczytane; nie zmienia wcześniejszych dat i nie dotyka innych kont.
Nowe zdarzenia, zapisane po wykonaniu aktualizacji, pozostają nieprzeczytane.

## SignalR

Istniejący hub: `/hubs/notifications`, uwierzytelnienie JWT
(`accessTokenFactory` klienta SignalR). Serwer przypisuje połączenie do prywatnej
grupy identyfikatora zalogowanego konta. Klient nie wybiera grupy ani odbiorcy.

Zdarzenie `NotificationCreated` zawiera jeden argument: dokładnie ten sam
`NotificationDto` co element listy REST (siedem pól z przykładu powyżej).
Wszystkie aktywne połączenia odbiorcy mogą otrzymać to samo powiadomienie.
Frontend powinien scalać rekordy po `id`.

```ts
connection.on("NotificationCreated", notification => {
  // Dodaj lub odśwież rekord po notification.id, odśwież licznik z REST.
});
connection.onreconnected(async () => {
  // Pobierz listę i unread-count z REST, aby odzyskać pominięte zdarzenia.
});
```

REST jest źródłem danych po zalogowaniu i ponownym połączeniu.
Zdarzenie SignalR jest wysyłane po zatwierdzeniu zapisu w bazie.
Nie ma gwarancji ponownej emisji zdarzenia po awarii serwera/połączenia;
trwały rekord pozostaje dostępny przez REST. Nie ma osobnego zdarzenia odczytu:
po `read`/`read-all` frontend odświeża listę i licznik (również po zmianie karty).

## Spójność i prywatność

- Wizyty tworzone/edytowane i ich powiadomienia są zapisywane w jednej jawnej
  transakcji. Przy odwołaniu oba rekordy zapisuje pojedyncze `SaveChanges`.
  Nieudany zapis powiadomienia wycofuje również zmianę wizyty.
- `eventId` pochodzi z serwerowego `Appointment.ConcurrencyStamp` skutecznej
  zmiany. Unikalny indeks `(eventId, recipientId)` chroni przed duplikatami;
  serwis dodatkowo sprawdza zapisane i aktualnie przygotowane rekordy.
- Dla pacjenta używane jest `Appointment.PatientUserId`, a dla terapeuty
  `TherapistProfile.UserId`. Zakończenie współpracy i ponowne powiązanie tej
  samej karty z innym kontem nie przenoszą wcześniejszej własności.
- Treść jest stała: bez PESEL-u, wiadomości pacjenta, powodu wizyty i prywatnych
  notatek. DTO nie ujawnia odbiorcy ani identyfikatora karty pacjenta.
- Awaria SignalR jest logowana i nie cofa zapisu. Dostarczenie ma limit 5 sekund
  i nie zależy od anulowania żądania HTTP po zatwierdzeniu transakcji.
- Portalowe powiadomienia nie zależą od usług e-mail. Przypomnienia e-mail
  pozostają bez zmian.

## Migracja i weryfikacja

Migracja `AddAppointmentNotifications` dodaje tabelę `notifications`, klucze obce
do wizyty i konta odbiorcy oraz indeksy dla deduplikacji, listy i licznika.
Nie tworzy historycznych powiadomień dla istniejących wizyt.
Przed uruchomieniem nowego backendu zastosuj migrację zgodnie z dotychczasowym
procesem wdrożenia:

```sh
dotnet ef database update --project backend/PhysioFlow.Infrastructure --startup-project backend/PhysioFlow.Api
```

Testy `NotificationsTests` obejmują zdarzenia, rzeczywisty WebSocket/SignalR,
awarię wysyłki, izolację dwóch pacjentów i terapeutów, zachowanie właściciela,
odczyty, licznik, paginację i deduplikację. `PostgresNotificationTests` sprawdzają
migrację w obie strony, indeks unikalny, równoległy odczyt, zapis atomowy przy
wymuszonej awarii i widoczność rekordów z osobnego połączenia przed emisją.

```sh
dotnet build backend/PhysioFlow.sln --configuration Release
dotnet test backend/PhysioFlow.sln --configuration Release --no-build
```

Testy PostgreSQL wymagają `PHYSIOFLOW_TEST_POSTGRES` wskazującego instancję
z prawem `CREATEDB`. Każda fabryka tworzy i usuwa własną losową bazę testową.
Bez tej zmiennej testy PostgreSQL są pomijane; istniejący Backend CI ustawia ją
i uruchamia je na PostgreSQL 16.

Poza zakresem: push przeglądarki, czat, załączniki, preferencje powiadomień
oraz zmiany mechanizmu przypomnień e-mail.
