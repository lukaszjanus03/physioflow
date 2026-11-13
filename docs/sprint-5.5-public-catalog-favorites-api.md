# Sprint 5.5, etap 2 — publiczny katalog i ulubieni (backend)

Kontrakt do integracji frontendu. Rozszerzamy istniejący `/api/therapists` i ten sam
stan publikacji z etapu 1. Nie ma drugiego mechanizmu publikowania ani nowego
wymagania zdjęcia lub specjalizacji. Formularz profilu, współpraca i rezerwacje
zachowują dotychczasowe kontrakty.

## Publiczne odczyty

Bez tokenu, także dla zalogowanych pacjentów i terapeutów:

- `GET /api/therapists` — stronicowana lista.
- `GET /api/therapists/{id}` — szczegóły, gdzie `id` to ID profilu terapeuty.
- `GET /api/therapists/specializations` — tablica `{ "code", "label" }` z etapu 1.
- `GET /api/therapists/{id}/photo` — istniejący endpoint obrazu WebP.

Lista i szczegóły zwracają dotychczasowy `TherapistCatalogEntryDto`, identyczny dla
wszystkich odbiorców. Wyłącznie: `id`, `firstName`, `lastName`, `city`,
`practiceName`, `bio`, `marketplace`, `photoUrl`, `averageRating`, `reviewCount`. Podsumowania
ocen opisuje [etap 3](sprint-5.5-therapist-reviews-api.md). `marketplace` zawiera
`specializations` (zawsze tablica), `experience`, `education`, `street`,
`buildingNumber`, `postalCode`, `publicPhoneNumber`, `publicEmail`,
`organizationalInfo`, `firstVisitPreparation`. Pola opcjonalne mogą być `null`.
Brak zdjęcia oznacza `photoUrl: null`. Teksty należy wyświetlać jako zwykły tekst.
Pełne limity zapisu opisuje [etap 1](sprint-5.5-therapist-profiles-api.md).

Publiczny e-mail i telefon pochodzą wyłącznie z jawnie zapisanych pól marketplace.
Nie kopiujemy e-maila Identity, starego telefonu, dowolnej starej specjalizacji
ani numeru uprawnień. Nie zwracamy ID konta Identity, danych pacjentów, dokumentacji,
wizyt ani stanu ulubionych i współpracy. Frontend pobiera stan ulubionych osobno
i zestawia go po ID profilu.

Widoczne są tylko opublikowane, nieusunięte profile aktywnych kont z miejscowością
i nazwą gabinetu. Ukryty, usunięty, nieaktywny i nieistniejący profil daje taki sam
`404` szczegółów. Dotychczasowy wyjątek zdjęcia pozostaje: właściciel z tokenem
może zobaczyć zdjęcie własnego szkicu; inni otrzymują `404`.

Odpowiedzi katalogu, szczegółów, słownika, zdjęć i ulubionych mają
`Cache-Control: no-store` (także przy `404` ukrytego profilu). Backend nie cache'uje
tych odczytów: każde żądanie sprawdza bieżącą publikację i aktywność konta.
Frontend/CDN/service worker powinny respektować `no-store`, nie przechowywać
kopii publicznego profilu lub zdjęcia i usuwać dane widoku po odpowiedzi `404`.
Ukrycie nie może cofnąć danych już pobranych i wyświetlonych przez odbiorcę.

## Wyszukiwanie i filtry

`GET /api/therapists?search=anna&city=Krak%C3%B3w&specialization=sports&pageNumber=1&pageSize=20`

- `search`: opcjonalnie, maks. 100 znaków. Ignoruje wielkość liter, spacje na
  brzegach i nadmiar białych znaków między słowami. Każde słowo musi pasować
  do fragmentu imienia, nazwiska, nazwy gabinetu lub miejscowości. Zachowujemy
  dotychczasowe wyszukiwanie po mieście, aby nie zmieniać zachowania portalu.
- `city`: opcjonalnie, maks. 100 znaków. Dokładna nazwa miasta po przycięciu
  brzegów, bez rozróżniania wielkości liter; nie jest wyszukiwaniem fragmentu.
- `specialization`: opcjonalnie, maks. 50 znaków, jeden kod ze słownika, np.
  `sports`. Przycinamy brzegi; kod rozróżnia wielkość liter (`SPORTS` daje `400`).
  Nie filtrujemy po etykiecie ani po starym dowolnym opisie specjalizacji.
- Puste / białe `search`, `city`, `specialization` oznaczają brak danego filtra.
  Limity długości dotyczą przesłanych wartości, przed przycięciem.
- Wszystkie filtry i słowa wyszukiwania łączymy przez AND.
- `pageNumber`: domyślnie 1, minimum 1. Odrzucamy również wartości powodujące
  przekroczenie zakresu przesunięcia `int` przy danym `pageSize`.
- `pageSize`: domyślnie 20, zakres 1–50. Błędna wartość daje `400`, nie ciche
  obcięcie. Nieprawidłowy format liczbowy również daje `400`.
- Sortowanie: nazwisko ASC, imię ASC, ID profilu ASC. Kolejność tekstów wynika
  z kolacji PostgreSQL. Wyszukiwanie nie usuwa polskich znaków diakrytycznych.

Odpowiedź `200`:

```json
{
  "items": [],
  "totalCount": 0,
  "pageNumber": 1,
  "pageSize": 20,
  "totalPages": 0,
  "hasPrevious": false,
  "hasNext": false
}
```

Strona poza wynikami także daje `200` i puste `items`, z rzeczywistym
`totalCount`. Walidacja aplikacyjna zwraca `400 application/problem+json` z mapą
`errors`, np. `errors.city`, `errors.specialization`, `errors.pageSize`.
Format błędów wiązania parametrów liczbowych pozostaje standardowy dla ASP.NET.

## Limit publicznych żądań

Początkowa konfiguracja: **120 żądań / 1 minutę / adres IP**, wspólnie dla listy,
szczegółów, słownika i zdjęć. Dotyczy także żądań zalogowanych i odpowiedzi `404`.
Bez kolejki, po przekroczeniu `429 application/problem+json` i `Retry-After`
w sekundach. Odpowiedź `429` również ma `no-store`.

Konfiguracja: `RateLimiting:PublicTherapistCatalog:PermitLimit` i `WindowMinutes`
(zmienne środowiskowe: `RateLimiting__PublicTherapistCatalog__PermitLimit`,
`RateLimiting__PublicTherapistCatalog__WindowMinutes`). Limit jest lokalny dla
instancji API, zgodnie z dotychczasowym mechanizmem limiterów. Za reverse proxy
należy skonfigurować zaufane forwarded headers, aby backend otrzymywał właściwy
adres klienta; obecnie bez tego partycja opiera się na adresie proxy.

## Ulubieni konta pacjenta

Wszystkie endpointy wymagają ważnego tokenu i roli `Patient`. Konto właściciela
pochodzi wyłącznie z tokenu, bez parametru ID konta. Działają także dla pacjenta
bez karty i współpracy. Brak sesji daje `401`, inna rola `403`.

- `GET /api/patient-accounts/me/favorite-therapists?pageNumber=1&pageSize=20`
  — `200`, standardowy obiekt paginacji jak wyżej. Domyślne i dopuszczalne
  wartości parametrów identyczne jak w katalogu; błędy dają `400`.
- `PUT /api/patient-accounts/me/favorite-therapists/{therapistId}`
  — bez body, `204` po dodaniu i przy powtórzeniu. `therapistId` oznacza ID
  profilu, nie konta Identity. Terapeuta musi być aktualnie dostępny publicznie;
  w przeciwnym razie `404`, także gdy pacjent wcześniej go zapisał.
  Równoczesne dodanie tej samej pary daje obu żądaniom `204` i jeden rekord.
- `DELETE /api/patient-accounts/me/favorite-therapists/{therapistId}`
  — `204`, także gdy wpisu już nie ma, ID jest nieznane albo profil ukryto.
  Usuwa wyłącznie wpis bieżącego konta.

Element listy dostępnego ulubionego:

```json
{
  "therapistId": "53d8b941-46d6-493c-a09f-d37135532130",
  "createdAt": "2026-11-12T12:00:00Z",
  "isAvailable": true,
  "profile": {
    "id": "53d8b941-46d6-493c-a09f-d37135532130",
    "firstName": "Anna",
    "lastName": "Nowak",
    "city": "Kraków",
    "practiceName": "Gabinet Ruch",
    "bio": null,
    "marketplace": {
      "specializations": ["sports"],
      "experience": null,
      "education": null,
      "street": null,
      "buildingNumber": null,
      "postalCode": null,
      "publicPhoneNumber": null,
      "publicEmail": null,
      "organizationalInfo": null,
      "firstVisitPreparation": null
    },
    "photoUrl": null
  }
}
```

Po ukryciu, usunięciu lub dezaktywacji konta zachowujemy miejsce na liście:

```json
{
  "therapistId": "53d8b941-46d6-493c-a09f-d37135532130",
  "createdAt": "2026-11-12T12:00:00Z",
  "isAvailable": false,
  "profile": null
}
```

Nie zwracamy zapamiętanej nazwy, kontaktów, zdjęcia ani przyczyny niedostępności.
Frontend może pokazać „Terapeuta niedostępny” i przycisk usunięcia. Po ponownym
opublikowaniu / aktywacji profil znów jest dostępny bez ponownego dodawania.
Niedostępne wpisy wliczają się do `totalCount` i paginacji. Sortowanie:
`createdAt` DESC, `therapistId` ASC; ponowny PUT nie zmienia daty zapisu.

Ulubieni nie tworzą karty pacjenta ani prośby o współpracę, nie przyznają dostępu
do dokumentacji lub rezerwacji. Usunięcie ulubionego nie kończy współpracy i nie
odwołuje wizyt. Prośby nadal wymagają konta pacjenta, a rezerwacje aktywnej
współpracy.

## Migracja i sprawdzenie

`20261112185308_AddFavoriteTherapists` dodaje wyłącznie `favorite_therapists`:
`patient_user_id`, `therapist_id`, `created_at`. Złożony klucz główny uniemożliwia
duplikaty. FK prowadzą do konta Identity i profilu terapeuty. Usunięcie konta
usuwa jego ulubionych; fizyczne usunięcie terapeuty z zapisanymi ulubionymi jest
blokowane przez FK (aplikacja stosuje soft delete). Migracja nie zmienia
istniejących profili, kart, współprac ani wizyt.

```powershell
dotnet ef database update --project backend/PhysioFlow.Infrastructure --startup-project backend/PhysioFlow.Api
$env:PHYSIOFLOW_TEST_POSTGRES = 'Host=localhost;Port=5432;Database=postgres;Username=physioflow;Password=dev'
dotnet test backend/PhysioFlow.sln --configuration Release --collect:'XPlat Code Coverage'
```

Testy PostgreSQL tworzą i usuwają wyłącznie własne losowe bazy (uprawnienie
`CREATEDB`). Sprawdzają migrację z poprzedniego etapu, filtry i tablicę
specjalizacji na rzeczywistym providerze, równoczesny INSERT wymuszony barierą,
izolację i usuwanie oraz ukrycie zapisanego profilu. Pozostałe testy sprawdzają
publiczne DTO, anonimowe odczyty, cache, limity, walidację, ukryte/usunięte/
nieaktywne profile i zdjęcia, paginację, role i brak wpływu na współpracę/wizyty.
