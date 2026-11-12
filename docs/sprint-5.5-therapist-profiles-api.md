# Sprint 5.5, etap 1 — profile terapeutów (backend)

Rozszerzenie istniejącego kontraktu `/api/therapists`. Dotychczasowe pola i role
katalogu pozostają bez zmian. Nie dodajemy statusu „zweryfikowany”: opis wykształcenia,
specjalizacji ani zapis zdjęcia nie stanowią weryfikacji kwalifikacji.

## Edycja i odczyt

- `GET /api/therapists/me/profile` — własny profil, rola `Therapist`.
- `PUT /api/therapists/me/profile` — zapis własnego profilu, rola `Therapist`.
- `GET /api/therapists` — katalog opublikowanych profili, rola `Patient`.
- `GET /api/therapists/{id}` — szczegóły opublikowanego profilu, rola `Patient`.
- `GET /api/therapists/specializations` — słownik, role `Therapist` i `Patient`.

Właściciel zawsze pochodzi z JWT. Nie ma endpointu edycji dowolnego profilu po ID;
przesłane `id` lub `userId` nie wybierają właściciela. Profile nieopublikowane,
usunięte oraz nieaktywnych kont nie pojawiają się w katalogu.

Przykładowy zapis (wszystkie nowe pola są opcjonalne):

```json
{
  "firstName": "Anna",
  "lastName": "Nowak",
  "city": "Kraków",
  "practiceName": "Gabinet Ruch",
  "bio": "Fizjoterapia dorosłych i sportowców.",
  "isPublished": false,
  "marketplace": {
    "specializations": ["orthopedic", "sports", "manual-therapy"],
    "experience": "10 lat praktyki w rehabilitacji ortopedycznej.",
    "education": "AWF Kraków, fizjoterapia, studia magisterskie.",
    "street": "Długa",
    "buildingNumber": "12/3",
    "postalCode": "30-001",
    "publicPhoneNumber": "+48 123 456 789",
    "publicEmail": "gabinet@example.pl",
    "organizationalInfo": "Parking od podwórza. Winda i wejście bez schodów.",
    "firstVisitPreparation": "Zabierz wyniki badań i wygodny strój."
  }
}
```

Miejscowość pozostaje w dotychczasowym polu `city`; nie ma drugiej kopii w adresie.
`marketplace` jest opcjonalnym **całym zestawem nowych danych**. Pominięcie tego
obiektu albo `marketplace: null` zachowuje nowe dane (zgodność starego formularza).
Przesłanie obiektu zastępuje cały zestaw: pominięte / `null` / puste pola tekstowe
są czyszczone, pominięte lub `null` `specializations` staje się `[]`.
Do edycji jednego pola frontend powinien przesłać obiekt z pozostałymi aktualnymi
wartościami. `marketplace: {}` czyści nowe dane. Zapis profilu nie zmienia zdjęcia.

Własny profil i wpisy katalogu otrzymują dodatkowo `marketplace` (zawsze obiekt,
`specializations` zawsze tablica) i `photoUrl` (ścieżka API albo `null`). Własny
profil nadal zwraca `isPublished` oraz `missingFieldsForPublication`.
Teksty są przycinane na brzegach, puste wartości są przechowywane jako `null`.

`publicPhoneNumber` i `publicEmail` są publikowane wyłącznie po ich jawnym
przesłaniu w `marketplace`. Nigdy nie kopiujemy ich z Identity ani ze starego
`TherapistProfile.PhoneNumber`. Frontend powinien jasno opisać, że są publiczne.
Stare `Specialization`, `PhoneNumber` i `LicenseNumber` pozostają w bazie bez zmian;
nie ujawniamy ich automatycznie jako nowych danych marketplace. Dowolnego starego
opisu specjalizacji nie przypisujemy automatycznie do słownika.

## Limity i walidacja

- `firstName`, `lastName`: wymagane przy zapisie, maks. 100 znaków każde.
- `city`: maks. 100; `practiceName`: maks. 150; `bio`: maks. 500.
- `experience`, `education`, `organizationalInfo`, `firstVisitPreparation`:
  maks. 2000 znaków każde, zwykły tekst (frontend wyświetla jako tekst, bez HTML).
- `street`: maks. 150; `buildingNumber`: maks. 20 (np. `12A/3`).
- `postalCode`: pusty albo polski format `00-000` (6 znaków).
- `publicEmail`: maks. 254, pojedynczy adres e-mail bez nazwy wyświetlanej.
- `publicPhoneNumber`: maks. 30 znaków, 7–15 cyfr ASCII, opcjonalny `+` na początku;
  dodatkowo dozwolone spacje, nawiasy i myślniki.
- `specializations`: od 0 do 8 unikalnych kodów z poniższego słownika;
  kody są rozróżniane wielkością liter. Nieznane kody, duplikaty i elementy `null`
  są błędem. Cała tablica `null` oznacza pusty wybór.

Błędy: HTTP 400, `application/problem+json`, dotychczasowy obiekt `errors`, np.
`errors["marketplace.publicEmail"]` lub `errors["marketplace.specializations"]`.
Niepoprawny zapis nie zmienia danych profilu.

## Uzgodniony słownik specjalizacji

Słownik przyjęty z Łukaszem dla etapu 1; stabilne kody są przechowywane w bazie.
`GET /api/therapists/specializations` zwraca tablicę `{ "code", "label" }`:

- `orthopedic` — Fizjoterapia ortopedyczna.
- `neurological` — Fizjoterapia neurologiczna.
- `sports` — Fizjoterapia sportowa.
- `pediatric` — Fizjoterapia pediatryczna.
- `urogynecological` — Fizjoterapia uroginekologiczna.
- `geriatric` — Fizjoterapia geriatryczna.
- `respiratory` — Fizjoterapia oddechowa.
- `manual-therapy` — Terapia manualna.

## Publikacja

Zachowujemy dotychczasowe wymagania: niepuste `firstName`, `lastName`, `city`,
`practiceName`; nazwisko nie może być placeholderem `(uzupełnij)`. Publikacja
wymaga jawnego `isPublished: true`. Zdjęcie, specjalizacje, biografia, szczegóły
adresu i wszystkie nowe pola są opcjonalne także dla już opublikowanych profili.
To utrzymuje zgodność i nie ukrywa wcześniej opublikowanych profili po migracji.

`GET /me/profile` oraz poprawny zapis zwracają `missingFieldsForPublication`,
np. `["city", "practiceName"]`; pusta tablica oznacza gotowość do publikacji.
Próba publikacji niepełnych danych daje 400 z błędami konkretnych pól i nie
zmienia profilu. `isPublished: false` ukrywa profil oraz jego zdjęcie przed innymi.
Ukrywanie nie kasuje danych i nie usuwa pliku zdjęcia.

## Zdjęcie profilowe

- `PUT /api/therapists/me/profile/photo` — dodanie / wymiana, rola `Therapist`.
  `multipart/form-data`, jedno pole plikowe **`file`**; odpowiedź 200 z całym
  `TherapistOwnProfileDto` i `photoUrl`.
- `DELETE /api/therapists/me/profile/photo` — usunięcie, rola `Therapist`.
  Odpowiedź 200 z profilem i `photoUrl: null`. Ponowne usunięcie też daje 200.
- `GET /api/therapists/{id}/photo` — zwraca `image/webp`. Zdjęcia opublikowanych,
  aktywnych profili są publiczne i mogą być użyte jako `src` obrazu.
  Właściciel może także pobrać zdjęcie własnego szkicu z tokenem Bearer
  (frontend pobiera blob z autoryzacją). Dla innych szkic / ukryty / usunięty /
  nieaktywny profil / brak zdjęcia daje ten sam 404. Nie zwracamy klucza storage
  ani podpisanego adresu R2. Nagłówki: `Cache-Control: no-store`,
  `X-Content-Type-Options: nosniff`.

Obsługiwane wejście: JPEG, PNG, WebP; od 1 bajta do **5 MiB = 5242880 bajtów**.
Format rozpoznajemy z zawartości przez dekoder SkiaSharp, bez zaufania do nazwy
i MIME przeglądarki. SVG, GIF, BMP, pliki tekstowe i niedekodowalne obrazy są
odrzucane, nawet jeżeli mają nazwę `.jpg` i MIME `image/jpeg`.
Limit dotyczy rzeczywiście odczytanych bajtów. Dodatkowy limit to 20 mln pikseli
przed pełnym dekodowaniem. Serwer dopuszcza niewielki narzut opakowania multipart.

Obraz jest dekodowany do pikseli, obracany według EXIF i kodowany ponownie jako
statyczny WebP (jakość 85), maks. 1024 × 1024 z zachowaniem proporcji, bez
powiększania. Dla animowanego PNG/WebP zapisujemy pierwszą klatkę.
Nowy plik nie przenosi EXIF/GPS, XMP, IPTC ani profilu ICC oryginału.
Nie przechowujemy oryginalnego uploadu. Transformacja używa
[SkiaSharp](https://github.com/mono/SkiaSharp) 3.119.2, z dołączonym pakietem
`SkiaSharp.NativeAssets.Linux.NoDependencies` dla backendu i CI na Linuksie.

Najpierw powstaje nowy obiekt w R2, potem zapisujemy jego klucz w bazie, a na końcu
usuwamy poprzedni obiekt. Błąd walidacji, uploadu albo zapisu bazy nie usuwa starego
działającego zdjęcia. Klucz zdjęcia jest tokenem współbieżności: równoczesne
nieaktualne zapisy otrzymują 409. Błąd sprzątania starego pliku jest logowany i nie
cofa działającej wymiany. Po błędzie zapisu bazy albo sprzątania może pozostać
nieużywany obiekt R2; nie udostępniamy go przez API. Celowo nie kasujemy nowego
pliku przy niepewnym wyniku transakcji, żeby nie usunąć potencjalnie zatwierdzonego
zdjęcia. Przy usunięciu najpierw odpinamy klucz w bazie, więc plik przestaje być
dostępny przez API nawet przy awarii usuwania z R2. Bucket powinien być prywatny.

Błędy uploadu: 400 (`errors.file`) przy złym obrazie / rozmiarze; przy przekroczeniu
limitu całego żądania serwer może zwrócić 413. Brak konfiguracji lub awaria R2 daje
503 (`code: "storage_unavailable"`) ze stałym komunikatem bez danych dostawcy.
Edycja profilu i odczyt profili bez zdjęć działają także bez konfiguracji R2.

## Storage, migracja i weryfikacja

Używamy istniejącego `IStorageService` / `R2StorageService`. Konfiguracja jest
zewnętrzna: `R2__AccountId`, `R2__AccessKey`, `R2__SecretKey`, `R2__BucketName`
w zmiennych środowiskowych lub lokalnych user-secrets. Nigdy nie umieszczaj
wartości sekretów w repozytorium ani w odpowiedziach API.

Migracja `ExpandTherapistMarketplaceProfile` dodaje kolumny nullable oraz
`specializations text[] NOT NULL DEFAULT ARRAY[]::text[]`. Nie nadpisuje starych
kolumn, nazw, biografii, danych kontaktowych ani `is_published`.

Zastosowanie migracji na docelowym środowisku (po standardowym backupie bazy):

```powershell
dotnet ef database update --project backend/PhysioFlow.Infrastructure --startup-project backend/PhysioFlow.Api
```

Testy: odczyt/zapis nowych pól, walidacja, izolacja właścicieli, zgodność starego
klienta, publikowanie/ukrywanie, upload trzech formatów, rozpoznawanie po treści,
odrzucanie SVG/GIF/uszkodzonych/za dużych plików, usuwanie metadanych i orientacja,
wymiana/usuwanie, błędy storage i bazy, autoryzacja zdjęć. Testy PostgreSQL
sprawdzają zachowanie istniejących opublikowanych i nieopublikowanych profili
po migracji, rzeczywisty zapis tablicy specjalizacji, projekcję katalogu i konflikt
współbieżnych zmian zdjęcia. Korzystają z osobnych losowych baz testowych,
wymagają połączenia `PHYSIOFLOW_TEST_POSTGRES` z uprawnieniem `CREATEDB`.

```powershell
$env:PHYSIOFLOW_TEST_POSTGRES = 'Host=localhost;Port=5432;Database=postgres;Username=physioflow;Password=dev'
dotnet test backend/PhysioFlow.sln --configuration Release
```

CI już ustawia PostgreSQL i tę zmienną. Testy storage korzystają z kontrolowanej
implementacji testowej; testy nie łączą się z produkcyjnym R2.
