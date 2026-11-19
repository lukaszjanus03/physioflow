# Sprint 8, etap 2 — statystyki gabinetu i zgłoszony ból pacjenta

## Istniejące API i źródła danych

`AnalyticsController` był szkieletem bez endpointów. Dashboard frontendu pobierał liczbę kart przez stronicowane `GET /api/patients`; wizyty i dziennik mają już osobne API. Nowe endpointy korzystają z tych samych kart, wizyt oraz `ExerciseJournalEntry`, bez migracji, nowych zapisów lub kopiowania danych.

Podgląd [dziennika terapeuty](sprint-7-stage-2-therapist-exercise-journal-api.md) i wykres korzystają ze wspólnej kontroli dostępu `TherapistJournalAccess`. Dotychczasowe endpointy dziennika i ich filtry pozostają dostępne z dotychczasowym kontraktem. Starszy `PatientDiaryEntry`, mapy ciała i dokumentacja medyczna nie są źródłem ocen tego wykresu.

## Autoryzacja i prywatność

Oba endpointy wymagają `Authorization: Bearer <jwt>` z rolą `Therapist` i zwracają `Cache-Control: no-store`. Właściciel statystyk wynika z sesji. Klient nie wybiera terapeuty parametrem ani identyfikatorem konta.

Dashboard wymaga istniejącego, nieusuniętego profilu terapeuty. Statystyki dotyczą wyłącznie jego kart i wizyt. Liczenie kart nie wymaga konta portalowego ani aktywnej współpracy.

Wykres wymaga wszystkich dotychczasowych warunków podglądu dziennika:

- Nieusunięty profil terapeuty, własna nieusunięta i niearchiwalna karta pacjenta.
- Obecne powiązanie karty z aktywnym, potwierdzonym kontem z rolą `Patient`.
- Współpraca `Accepted` zgodna z kartą, terapeutą i kontem.
- Nieusunięte wpisy tego konta i karty, z `PublicationLinkVersion` zgodnym z obecnym `AccountLinkVersion`.

Brak karty, cudza karta, archiwizacja, zakończenie współpracy lub nieprawidłowe powiązanie zwracają `404`. Ponowne powiązanie, także z tym samym kontem, rozpoczyna nowy okres historii widocznej terapeucie. Stare wpisy nie trafiają do nowego wykresu. Cofnięcie publikacji planu i usunięcie planu lub ćwiczenia nie usuwają dostępnej historii dziennika. Przywrócenie karty z archiwum udostępnia bieżący okres, o ile pozostałe warunki nadal są spełnione.

Odpowiedzi zawierają wyłącznie zakres, strefę i agregaty. Nie zwracają komentarzy, nazw planów i ćwiczeń, snapshotów, powodów wizyt, wiadomości ani dokumentacji. Grupowanie, średnie i liczniki są obliczane w SQL; serwer nie pobiera całych wpisów do agregacji w pamięci. Logowanie pipeline zapisuje tylko nazwy operacji.

## Wspólny zakres dat

Oba endpointy przyjmują `from` i `to` w formacie `YYYY-MM-DD`:

- Należy podać obie granice albo pominąć obie. Pojedyncza granica daje `400 validation_failed`.
- Bez parametrów zakres obejmuje bieżący tydzień, poniedziałek–niedziela, ustalony według aktualnej daty w `Europe/Warsaw`. Dotyczy to również domyślnego widoku wykresu.
- Obie daty są włączone. Jednodniowy zakres jest poprawny.
- Wymagane `1900-01-01 <= from <= to <= 9999-12-30`.
- Maksymalnie **366 dni łącznie z obiema granicami**: `to.DayNumber - from.DayNumber + 1 <= 366`. Np. `2024-01-01`–`2024-12-31` jest poprawny.
- Przyszłe zakresy są dozwolone. Brak danych daje prawidłową pustą odpowiedź, a nie błąd.
- Niepoprawna data, odwrotny lub zbyt długi zakres daje `400` z kodem `validation_failed`.

Odpowiedź zawsze zawiera efektywne `from`, `to` i `timeZone: "Europe/Warsaw"`. Limit górnej daty pozwala bez przepełnienia wyznaczyć następną północ.

## Dashboard — `GET /api/analytics/dashboard`

Przykład: `GET /api/analytics/dashboard?from=2026-11-16&to=2026-11-22`.

```json
{
  "from": "2026-11-16",
  "to": "2026-11-22",
  "timeZone": "Europe/Warsaw",
  "activePatientCount": 12,
  "appointments": {
    "scheduled": 8,
    "completed": 14,
    "cancelled": 2,
    "noShow": 1,
    "total": 25
  }
}
```

`activePatientCount` to liczba obecnie niearchiwalnych, nieusuniętych kart tego terapeuty, również bez powiązanego konta pacjenta. To stan bieżący, niezależny od zakresu wizyt, daty utworzenia karty i historycznego stanu archiwizacji. Zmiana zakresu nie zmienia tego licznika; archiwizacja karty zmienia go od razu.

Wizyty są liczone według **początku** `StartsAt`, z wyłączeniem logicznie usuniętych wizyt. Granice SQL to `[północ from w Warszawie, północ dnia po to w Warszawie)`, przeliczone osobno na UTC. Dni zmiany czasu mają odpowiednio 23 lub 25 godzin. Wizyta zaczynająca się przed zakresem nie jest liczona, nawet gdy kończy się w zakresie; start dokładnie na dolnej granicy wchodzi do wyniku, start na górnej granicy wyłącznej już nie.

Statusy odpowiadają istniejącym `Scheduled`, `Completed`, `Cancelled`, `NoShow`. Liczony jest obecny zapisany status; backend nie wyprowadza odbycia wizyty z upływu czasu. `total` jest sumą czterech liczników. Historia wizyt karty zarchiwizowanej nadal jest liczona w odpowiednim zakresie. Statystyki nie wymagają aktywnej współpracy ani konta portalowego pacjenta.

Jeśli w zakresie nie ma wizyt, cztery liczniki i `total` wynoszą `0`. `activePatientCount` nadal oznacza bieżący stan kart i może być dodatni. Terapeuta bez kart i wizyt dostaje same zera.

## Wykres — `GET /api/analytics/patients/{patientId}/pain-trend`

`patientId` jest UUID karty pacjenta. Przykład: `GET /api/analytics/patients/07572287-af21-415d-8e06-73caf69e0ea9/pain-trend?from=2026-11-16&to=2026-11-19`.

```json
{
  "from": "2026-11-16",
  "to": "2026-11-19",
  "timeZone": "Europe/Warsaw",
  "points": [
    { "date": "2026-11-16", "averagePainLevel": 2.5, "ratingCount": 2 },
    { "date": "2026-11-17", "averagePainLevel": null, "ratingCount": 0 },
    { "date": "2026-11-19", "averagePainLevel": 0, "ratingCount": 1 }
  ]
}
```

Punkty są posortowane rosnąco po `date`. Grupowanie używa **`PerformedOn`, dnia wykonania ćwiczeń zapisanego przez pacjenta**, a nie czasu utworzenia, edycji lub publikacji wpisu. `PerformedOn` jest datą kalendarzową w kontekście Warszawy i nie wymaga ponownej konwersji z UTC.

- Każda niepusta ocena `PainLevel` z dostępnego wpisu liczy się raz, także dla wielu ćwiczeń, planów i zgłoszeń tego samego dnia.
- `averagePainLevel` to średnia arytmetyczna zgłoszonych ocen 0–10, bez zaokrąglania po stronie API. `ratingCount` liczy tylko niepuste oceny.
- `0` jest pełnoprawną oceną braku bólu i wchodzi do średniej i licznika. Np. `0, 5, null` daje średnią `2.5` i licznik `2`.
- Jeśli dzień ma wpisy, lecz wszystkie oceny są `null`, zwracany jest punkt z `averagePainLevel: null` i `ratingCount: 0`.
- Dni bez dostępnych wpisów są pomijane. W przykładzie nie ma punktu dla 18 listopada. Serwer nie dodaje syntetycznych zer.
- Brak wpisów w zakresie daje `points: []`. Brak uprawnień daje `404`, a nie pustą serię.
- Uwzględniane są zgłoszone oceny z wszystkich statusów ćwiczeń (`Completed`, `Partial`, `Skipped`); status nie zastępuje oceny bólu.
- Edycja lub usunięcie wpisu zmienia wynik kolejnego odczytu. Wycofanie publikacji planu nie usuwa istniejących ocen.

Wykres przedstawia **zgłoszony ból**, bez oceny skuteczności leczenia i bez automatycznych wniosków medycznych.

## Integracja frontendu i błędy

Frontend powinien używać powyższych agregatów zamiast pobierania stron wizyt i wpisów do samodzielnego liczenia. Klucze cache muszą uwzględniać konto, zakres i — dla wykresu — kartę pacjenta. Po zmianie konta, archiwizacji, zakończeniu współpracy lub zmianie powiązania należy usuwać nieaktualne dane wykresu. `null` należy pokazać jako brak oceny, a `0` na osi bólu; nie należy wypełniać brakujących dat zerami. Dla wykresu nie należy włączać łączenia przez brak ocen (`connectNulls`).

Oba endpointy zwracają `200 OK` przy poprawnym odczycie, `401` bez ważnego uwierzytelnienia i `403` dla roli innej niż terapeuta. `404` oznacza brak dostępnego profilu lub dziennika. Błędy zakresu zwracają `400` z `code: "validation_failed"`; format szczegółów walidacji jest zgodny z istniejącym API.

## Testy

`AnalyticsTests` obejmuje izolację terapeutów i kart, aktualną archiwizację, soft delete, cztery statusy wizyt, puste wyniki, zmianę zakresu, granice początku wizyty, tydzień Warszawy przy innej dacie UTC, obie zmiany DST, walidację dat i limit 366 dni, wiele ocen, wartości `0` i `null`, luki, sortowanie, edycję ocen oraz zgodność dostępu dziennika po zakończeniu i wznowieniu współpracy lub zmianie konta.

`PostgresAnalyticsTests` uruchamia te scenariusze na odizolowanej bazie PostgreSQL i przechwytuje SQL, sprawdzając `GROUP BY`, `AVG`, `COUNT` oraz brak pobierania treści klinicznych w zapytaniach agregujących. Istniejące testy dziennika sprawdzają regresję wydzielonej kontroli dostępu.

```powershell
dotnet test backend/PhysioFlow.Tests/PhysioFlow.Tests.csproj --filter "FullyQualifiedName~AnalyticsTests|FullyQualifiedName~TherapistExerciseJournalTests"

# Lokalny PostgreSQL z docker-compose; testy tworzą i usuwają wyłącznie własne losowe bazy.
$env:PHYSIOFLOW_TEST_POSTGRES = 'Host=localhost;Port=5432;Database=postgres;Username=physioflow;Password=dev'
dotnet test backend/PhysioFlow.Tests/PhysioFlow.Tests.csproj
```

Bez `PHYSIOFLOW_TEST_POSTGRES` testy PostgreSQL są pomijane. CI ustawia ten parametr i uruchamia pełny zestaw na PostgreSQL 16.
