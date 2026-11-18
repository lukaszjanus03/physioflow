# Sprint 7 — etap 3: prywatny czat pacjent–terapeuta (backend)

Implementacja: REST `/api/chat/conversations`, SignalR `/hubs/chat`, migracja
`20261118164608_AddPrivateCooperationChat`. Nie wymaga zmian w infrastrukturze powiadomień.

## Kontekst rozmowy i dostęp

- Jedna rozmowa na zaakceptowaną prośbę o współpracę (`cooperationId`) i generację
  powiązania karty (`AccountLinkVersion`). Samo przypięcie konta do karty nie wystarcza:
  wymagana jest zaakceptowana współpraca z tym samym kontem i terapeutą.
- Identyfikatory obu uczestników są utrwalane podczas tworzenia rozmowy. Nie zmieniają
  się wraz z właścicielem karty. Nadawcę, odbiorcę, datę i kolejność wiadomości ustala serwer.
- REST i każda operacja huba sprawdzają dostęp. Aktualna aktywność konta oraz rola
  pacjenta/terapeuty są ponownie sprawdzane w bazie; pacjent wymaga potwierdzonego emaila.
  Rola Admin nie daje dodatkowych uprawnień do cudzych rozmów.
- Aktywna współpraca, niearchiwalna karta, zgodne konta i bieżąca generacja powiązania
  pozwalają wysyłać wiadomości. Brak potwierdzenia/aktywności odbiorcy blokuje nowe wysyłki.
- Archiwizacja zachowuje odczyt historii i potwierdzanie odczytu, ale blokuje wysyłkę.
  Przywrócenie karty w tej samej aktywnej współpracy i generacji ponownie pozwala wysyłać.
- Zakończenie współpracy zachowuje historię tylko do odczytu dla jej pierwotnych uczestników,
  także po ponownym przypięciu karty. Można potwierdzać odczyt. Stara rozmowa nigdy nie
  wraca do trybu wysyłania; nowa współpraca ma osobną rozmowę i osobne liczniki.
- Odpięcie konta poza zakończeniem współpracy natychmiast odbiera pacjentowi dostęp
  do rozmowy aktywnej współpracy. Terapeuta zachowuje odczyt. Ponowne przypięcie,
  nawet tego samego konta, tworzy nową generację i nowy kontekst rozmowy. Nie wznawia
  starej rozmowy ani nie udostępnia nowemu kontu wcześniejszej historii.
- Usunięcie karty/profilu przez soft delete, zmiana przypisanego terapeuty lub wyłączenie
  konta odbiera odpowiedni dostęp. Dane pozostają utrwalone; nie ma endpointów usuwania
  ani edycji wysłanych wiadomości. Usunięcie roli działa również dla otwartego połączenia.

## REST

Wymagany JWT Bearer z rolą Patient lub Therapist. Odpowiedzi czatu mają `Cache-Control: no-store`.
Identyfikatory to UUID. Nazwy JSON są camelCase; daty to UTC ISO 8601 z precyzją mikrosekund.

### Otwarcie rozmowy

`POST /api/chat/conversations`

```json
{ "cooperationId": "UUID" }
```

Zwraca `200 ChatConversationDto`. Powtórzenie zwraca tę samą rozmowę, również przy
równoczesnym otwarciu przez obie strony. Nowy kontekst wymaga aktywnej, poprawnie
powiązanej i niearchiwalnej współpracy. Lista zawiera tylko rozmowy już otwarte tym endpointem.

### Lista i szczegóły

- `GET /api/chat/conversations?pageNumber=1&pageSize=20`
- `GET /api/chat/conversations/{conversationId}`

Lista zwraca standardowy `PaginatedList<ChatConversationDto>`: `items`, `totalCount`,
`pageNumber`, `pageSize`, `totalPages`, `hasPrevious`, `hasNext`. Maksymalnie 100
elementów na stronę. Sortowanie: data ostatniej wiadomości (dla pustych rozmów data
utworzenia) malejąco, następnie ID rozmowy. Lista może zmieniać pozycje po nowych wiadomościach.

`ChatConversationDto` zawiera:

- `id`, `cooperationId`, `patientUserId`, `therapistUserId`;
- `canSend`, `readOnlyReason`: null albo `cooperation_ended`, `patient_archived`,
  `account_link_changed`, `participant_unavailable`;
- `lastMessage`: `ChatMessageDto` lub null; data ostatniej wiadomości to `lastMessage.sentAt`;
- `unreadCount`: liczba nieprzeczytanych wiadomości otrzymanych przez aktualnego użytkownika;
- `lastSequence`: ostatnia utrwalona pozycja w rozmowie;
- `readThrough`: pozycja ostatniej otrzymanej wiadomości potwierdzonej przez aktualnego użytkownika;
- `version`: nieprzezroczysty identyfikator zmiany stanu rozmowy. Nie jest kursorem historii.

### Historia i synchronizacja

`GET /api/chat/conversations/{conversationId}/messages?limit=50`

Opcjonalnie jeden z kursorów:

- `before=N`: wiadomości o `sequence < N`, do przeglądania starszej historii;
- `after=N`: wiadomości o `sequence > N`, do uzupełniania historii po reconnect, także `after=0`.

`limit` 1–100, domyślnie 50. `before` musi być dodatni, `after` nieujemny.
Nie można podać obu kursorów. Kursory są pozycjami w tej konkretnej rozmowie.

Odpowiedź `200 ChatHistoryDto`:

```json
{
  "items": [],
  "hasMore": false,
  "nextCursor": null,
  "lastSequence": 0
}
```

Bez kursora i z `before` wybierana jest najnowsza pasująca partia. Z `after` wybierana jest
najstarsza brakująca partia. W obu trybach `items` są rosnąco według `sequence`.
Gdy `hasMore=true`, `nextCursor` służy do kolejnego zapytania w tym samym trybie.
`sequence` jest przydzielane atomowo w ramach rozmowy; nie zależy od daty ani kolejności UUID.
Równoczesna wysyłka nie przesuwa granicy wcześniej pobranej strony starszej historii.

`lastSequence` jest informacją o stanie serwera, a nie potwierdzeniem pobrania wszystkich
wiadomości. Nie wolno przestawiać lokalnego kursora synchronizacji na tę wartość bez pobrania
brakujących stron. Synchronizację kontynuujemy od ostatniej faktycznie pobranej, ciągłej pozycji.

### Wysyłanie

`POST /api/chat/conversations/{conversationId}/messages`

```json
{ "requestId": "UUID", "content": "Treść wiadomości" }
```

`201 ChatMessageDto` dla nowego zapisu, `200 ChatMessageDto` dla identycznego ponowienia:

```json
{
  "id": "UUID",
  "conversationId": "UUID",
  "sequence": 1,
  "senderId": "UUID",
  "recipientId": "UUID",
  "requestId": "UUID",
  "content": "Treść wiadomości",
  "sentAt": "2026-11-18T12:00:00Z",
  "readAt": null
}
```

- Limit treści: 4000 jednostek UTF-16 przed przycięciem. Białe znaki na początku/końcu
  są usuwane. Null, pusty tekst, same białe znaki i znak NUL są odrzucane.
- `requestId` jest wymaganym niepustym UUID, unikalnym dla nadawcy we wszystkich rozmowach.
  Ta sama para nadawca/klucz i ta sama przycięta treść w tej samej rozmowie zwracają ten sam
  rekord. Inna treść lub rozmowa z tym kluczem daje `409 idempotency_key_reused`.
- Powtórzenie nie dopisuje wiadomości, nie zużywa limitu i nie emituje ponownie zdarzenia.
  Może zwrócić późniejszy `readAt`, jeżeli odbiorca zdążył potwierdzić odczyt.
- Ponowienie istniejącego zapisu po zakończeniu współpracy/archiwizacji nadal działa, o ile
  nadawca zachował odczyt rozmowy. Nie przywraca dostępu ani możliwości nowych wysyłek.
- Limit nowych wiadomości: domyślnie 30 na użytkownika na minutę UTC. Konfiguracja
  `Chat:MessagesPerMinute` (1–1000). Limit jest zapisany w PostgreSQL i wspólny dla rozmów,
  REST, huba oraz instancji aplikacji. `429 chat_rate_limit` ma `Retry-After: 60`.
- Nie przyjmujemy klientowskiego nadawcy, odbiorcy, daty ani statusu odczytu.

Przy timeout, utracie odpowiedzi lub niepewnym wyniku należy zachować ten sam `requestId`,
rozmowę i treść. Nowy UUID oznacza nową wiadomość. Nie generujemy nowego klucza automatycznie
po błędzie transportu, przełączeniu z huba na REST ani po reconnect.

### Odczyt

`POST /api/chat/conversations/{conversationId}/read`

```json
{ "throughMessageId": "UUID ostatniej faktycznie wyświetlonej otrzymanej wiadomości" }
```

Odpowiedź `200 ChatReadDto`: `conversationId`, `readerId`, `readThrough`, `unreadCount`.
Identyfikator musi należeć do otrzymanej wiadomości w tej rozmowie. Własna wiadomość
lub wiadomość z innej rozmowy daje 404. Serwer potwierdza wyłącznie otrzymane wiadomości
do wskazanej pozycji włącznie. Kursor nie cofa się, także przy równoczesnych żądaniach.
Ponowne potwierdzenie starej wiadomości nie oznacza nowych wiadomości jako przeczytanych.

Sam GET historii, otwarcie rozmowy i dostarczenie zdarzenia SignalR nie oznaczają odczytu.
Frontend wysyła potwierdzenie dopiero po wyświetleniu wiadomości. `ChatMessageDto.readAt`
jest serwerowym, utrwalonym statusem; nie należy wyliczać go z lokalnego doręczenia zdarzenia.

## SignalR

Połączenie `/hubs/chat`, JWT przez standardowy `accessTokenFactory` klienta SignalR.
Serwer ustala grupę wyłącznie na podstawie uwierzytelnionego ID. Brak metod dołączania
do grup lub wskazywania dowolnego odbiorcy. Wygaśnięcie autoryzacji zamyka połączenie.

Metody huba:

- `SendMessage(conversationId, { requestId, content })` → `{ message: ChatMessageDto, replayed: boolean }`;
- `MarkRead(conversationId, { throughMessageId })` → `ChatReadDto`;
- `GetHistory(conversationId, before, after, limit)` → `ChatHistoryDto`.
  Dla protokołu JSON należy przesłać wszystkie cztery argumenty, używając null dla brakujących kursorów.

Zdarzenia do aktualnie uprawnionych uczestników, również do pozostałych urządzeń nadawcy:

- `MessageCreated`: `{ conversationId, messageId, sequence }`;
- `MessagesRead`: `ChatReadDto`; `readerId` wskazuje czyj kursor i licznik zawiera zdarzenie;
- `ConversationUpdated`: `{ conversationId }`, po nowej wiadomości i potwierdzeniu odczytu.

Zdarzenia są wskazówkami do synchronizacji, bez treści wiadomości. Po `MessageCreated`
należy pobrać brakujące wiadomości przez API. Po `MessagesRead` odświeżyć lokalne statusy
przez API (kursor można zastosować monotonicznie jako wskazówkę). Po `ConversationUpdated`
odświeżyć szczegóły/listę; nie zwiększać licznika wyłącznie na podstawie liczby zdarzeń.

Zdarzenia wysyłamy dopiero po zatwierdzeniu zapisu. Ich utrata, opóźnienie, powtórzenie lub
zmieniona kolejność nie wpływają na dane. Awaria SignalR nie cofa zapisu ani nie zmienia
odpowiedzi na udaną wysyłkę/odczyt. Nie ma gwarancji dostarczenia ani trwałej kolejki zdarzeń.

Każda operacja ponownie sprawdza aktualne uprawnienia. Przed publikacją serwer ponownie
wyznacza uczestników uprawnionych do odczytu; otwarte połączenie nie przechowuje zezwolenia
na konkretną współpracę. Diagnostyka aplikacyjna czatu nie zapisuje treści ani tokenów.

## Reguły klienta

1. Zarejestrować handlery zdarzeń przed połączeniem.
2. Po połączeniu/reconnect odświeżyć listę i pobrać `after=ostatniaCiągłaLokalnaPozycja`
   w każdej aktualnie synchronizowanej rozmowie. Powtarzać po `nextCursor` do `hasMore=false`.
3. Scalać wiadomości po `id`, porządkować po `sequence`, scalać status odczytu monotonicznie.
   Rozmowy mają niezależne kursory. Po luce w zdarzeniach pobrać wszystkie brakujące strony.
4. W czasie awarii SignalR wysyłać, odczytywać i synchronizować przez REST. Odświeżać po
   odzyskaniu połączenia, powrocie do aplikacji i błędach dostępu/zmiany stanu współpracy.
5. Dla niepewnej wysyłki ponawiać niezmieniony klucz i treść; odpowiedź 200 oznacza
   potwierdzenie istniejącego zapisu. Nie tworzyć drugiego lokalnego rekordu po ponowieniu.
6. Odczyt potwierdzać ID faktycznie otrzymanej i widocznej wiadomości. Nie używać
   `lastSequence` ani ostatniego zdarzenia jako automatycznego potwierdzenia odczytu.
7. Po archiwizacji/zakończeniu współpracy odświeżyć szczegóły rozmowy. Serwer jest źródłem
   `canSend`; próba wysyłki ze starego interfejsu jest ponownie weryfikowana.
8. Treść traktować jako zwykły tekst; załączniki, HTML, edycja, usuwanie, typing i presence
   nie należą do tego kontraktu. Nie logować requestów z treścią ani adresów URL z JWT.

## Błędy

- 400 `validation_failed`: nieprawidłowa treść, UUID, paginacja lub kursory.
- 401: brak/niepoprawny/wygasły JWT; nieaktywne konto odrzucone przez middleware JWT.
- 403: brak wymaganej roli/aktywnego konta, także po zmianie uprawnień otwartego huba.
- 404: rozmowa, współpraca lub otrzymana wiadomość nie istnieje albo jest niedostępna.
  Odpowiedź nie ujawnia, czy wskazana cudza rozmowa istnieje.
- 409 `chat_not_active`: próba utworzenia rozmowy bez uprawnionej aktywnej współpracy.
- 409 `cooperation_ended`, `patient_archived`, `account_link_changed`,
  `participant_unavailable`: nowa wysyłka w rozmowie tylko do odczytu.
- 409 `idempotency_key_reused`: ten klucz był użyty dla innego żądania.
- 409 `concurrent_modification`: wyczerpane wewnętrzne ponowienia równoczesnego zapisu;
  odświeżyć dostęp i ponowić z tym samym kluczem. Baza nie przechowuje częściowej operacji.
- 429 `chat_rate_limit`: ponowić po czasie z `Retry-After`, zachowując klucz.

Hub zwraca bezpieczne kody `HubException`: `validation_failed`, `not_found`, `forbidden`,
`unauthorized`, powyższe kody 409/429, `operation_cancelled` lub `internal_error`.
Po błędzie transportu lub `internal_error` wynik wysyłki może być niepewny: ponowić przez REST
z tym samym kluczem. W SignalR prefiks komunikatu może być dodany przez bibliotekę protokołu.

## Migracja i testy

Nowe tabele: `chat_conversations`, `private_chat_messages`, `chat_send_quotas`.
Unikalne indeksy zabezpieczają kontekst rozmowy, sekwencję i parę nadawca/requestId.
Tokeny współbieżności chronią rozmowę, współpracę, kartę i limit użytkownika w jednej
transakcji SaveChanges. Wyścigi są ponawiane na aktualnych uprawnieniach.

Dotychczasowa tabela `chat_messages` pozostaje nietknięta. Nie przenosimy jej rekordów do
prywatnych rozmów: nie mają zweryfikowanego kontekstu współpracy i generacji konta.
Migracja nie zmienia ani nie usuwa dokumentacji lub danych pacjentów.

```powershell
dotnet ef database update --project backend/PhysioFlow.Infrastructure --startup-project backend/PhysioFlow.Api
dotnet test backend/PhysioFlow.sln
# Opcjonalnie testy z rzeczywistą izolowaną bazą PostgreSQL (konto z CREATEDB):
$env:PHYSIOFLOW_TEST_POSTGRES = 'Host=localhost;Database=postgres;Username=...;Password=...'
dotnet test backend/PhysioFlow.sln
```

Testy `ChatTests`, `ChatRealtimeTests`, `ChatPrivacyTests` i `PostgresChatTests` obejmują
historię, odczyt, limity, prywatność, REST/WebSocket, awarię transportu, migrację i wyścigi
zapisów, otwierania rozmowy, odczytu, limitów, archiwizacji oraz zakończenia współpracy.
Bazy testowe PostgreSQL mają losowe nazwy i są usuwane przez fabrykę po zakończeniu testu.
