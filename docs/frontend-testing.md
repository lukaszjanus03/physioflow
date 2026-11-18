# Testy frontendu

## Obecny zakres

Zestaw obejmuje udostepnianie planow i podsumowan wizyt ze Sprintu 6 oraz
dziennik realizacji cwiczen pacjenta ze Sprintu 7, etapu 1, i podglad terapeuty
z podsumowaniem ze Sprintu 7, etapu 2, prywatny czat z etapu 3 oraz
rozszerzenie eksportu PDF karty pacjenta ze Sprintu 8, etapu 1.
Nie jest to jeszcze pokrycie calego frontendu ani test backendowej
autoryzacji. Zwykle testy nie wymagaja backendu, bazy, kont, Resend ani R2.

Narzedzia:

- Vitest: funkcje i hooki.
- React Testing Library i user-event: zachowanie rzeczywistych komponentow.
- MSW: odpowiedzi HTTP na granicy sieci. Testy silnika czatu, formularza i cyklu
  polaczenia dodatkowo izoluja klienta API lub SignalR, aby sterowac kolejnoscia odpowiedzi.
- Playwright: rzeczywista przegladarka, aplikacja Next i symulowane odpowiedzi API.

## Uruchamianie

W katalogu `frontend`, po `npm ci`:

```powershell
npm test
npm run test:watch
npm run test:coverage
```

Pierwsza komenda wykonuje wszystkie testy jednostkowe, hookow i komponentow.
Druga pozostaje aktywna i ponawia testy po zmianie plikow.
Raport HTML pokrycia jest w `frontend/coverage/index.html`.
Pokrycie jest mierzone tylko dla plikow udostepniania, dziennika, czatu i eksportu wymienionych
w `vitest.config.mts`, nie dla calej aplikacji. Rozszerzaj te liste wraz z nowymi
obszarami. Nie interpretuj tego wyniku jako procentu pokrycia calego produktu.

Wybrany plik lub przypadek:

```powershell
npm test -- tests/components/patient-publication.test.tsx
npm test -- -t "does not retry a conflict"
```

Testy przegladarkowe:

```powershell
npx playwright install chromium
npm run build
npm run test:browser
```

Playwright uruchamia i zatrzymuje produkcyjny frontend na
`http://127.0.0.1:3100`. Port musi byc wolny; istniejacy serwer nie jest
przejmowany. Przed budowaniem zakoncz lokalny `next dev`, ktory uzywa tego samego
katalogu `.next`. Zwykly frontend mozesz ponownie uruchomic po testach.
Scenariusze sa wykonywane na desktopowym Chromium i mobilnym Chromium.

Raport przegladarkowy:

```powershell
npx playwright show-report
```

## Struktura

- `frontend/tests/setup.ts`: izolacja i cykl zycia MSW.
- `frontend/tests/render.tsx`: oddzielny QueryClient dla kazdego renderowania.
- `frontend/tests/fixtures/sharing.ts`: jawnie syntetyczne dane bez danych osobowych.
- `frontend/tests/unit`: kontrakty klienta API i bezpieczne komunikaty.
- `frontend/tests/hooks`: odswiezanie, anulowanie i usuwanie niedostepnej tresci.
- `frontend/tests/components`: panel publikacji i portal pacjenta.
- `frontend/e2e/*.browser.spec.ts`: integracja przegladarki z symulowanym API.

## Co sprawdzamy

- Eksport PDF: walidacja dat, wszystkie strony, zachowanie kolejnosci i wykrywalne zmiany zbioru.
- Przerwanie eksportu po bledzie, anulowanie, ponowna kontrola dostepu i brak niepelnego pliku.
- Rzeczywiste pobranie PDF w przegladarce, osobne sekcje kliniczne i dlugie tresci.

- Wybor zapisanej wersji, podglad i jawne potwierdzenie publikacji.
- Blokowanie publikacji bez spelnienia warunkow i podczas zapisu.
- Cofanie dostepu takze dla archiwalnej karty.
- Przesylanie tylko tokenu edycji i wybranego numeru wersji.
- Brak automatycznego ponawiania konfliktu.
- Brak ujawniania wewnetrznych pol dokumentacji i surowych bledow serwera.
- Listy, puste stany, bledy, ponowienie, adresy i nawigacja.
- Usuwanie tresci po utracie dostepu i zmienieniu zasobu.
- Ignorowanie spoznionych odpowiedzi oraz anulowanie po odmontowaniu.
- Brak zapisywania ladunkow klinicznych przez hook w pamieci przegladarki.
- Zwykly tekst zamiast wykonywania HTML z podsumowania.
- Walidacja dziennika: daty w Warszawie, dawka, status, bol null/0 i limit UTF-16.
- Zachowanie identycznego requestId i body po niepewnym zapisie dziennika.
- Jawne porownanie wpisu przy konflikcie edycji i ponowne potwierdzenie usuwania.
- Filtrowanie historycznych wpisow bez wymagania dostepu do aktualnego planu.
- Wstrzymanie odswiezania w tle na czas wypelniania formularza dziennika.
- Tylko odczyt dziennika przez terapeute, podsumowanie calego zakresu i filtry pozycji.
- Czat: limity UTF-16, allowlisty payloadow, ponowienie tego samego requestId i tresci.
- Synchronizacja historii bez pomijania luk, stronicowanie i monotoniczne potwierdzenia odczytu.
- Liczniki z serwera zamiast sumowania zdublowanych zdarzen SignalR.
- Rejestracja handlerow przed startem huba, ponowienie poczatkowego polaczenia i zmiana konta.
- Czat w obu panelach na desktopie i telefonie: REST fallback, zwykly tekst,
  odczyt widocznej wiadomosci, ochrona szkicu, readonly i usuwanie niedostepnej historii.
- Testy przegladarkowe czatu nie lacza sie z prawdziwym hubem; realny SignalR
  i autoryzacje nalezy sprawdzic recznie na dwoch kontach oraz testami backendu.
- Usuwanie calego podgladu po odmowie dostepu z dowolnego endpointu, takze szczegolow.
- Brak przywracania dostepu sama zmiana filtrow oraz izolacja pacjentow i kont.
- Testy przegladarkowe na desktopie i telefonie.

## Bezpieczenstwo i ograniczenia

MSW odrzuca niezdefiniowany ruch. Testy przegladarkowe przechwytuja wszystkie
zadania API, a inne zewnetrzne zadania blokuja. Nie uruchamiaja wysylek ani
operacji na rzeczywistej bazie i storage. Uzywaja jednorazowego profilu oraz
syntetycznej sesji pacjenta; jej token nie jest prawdziwym JWT.

Testy te NIE dowodza, ze backend prawidlowo odrzuca cudzy JWT lub zachowuje
historie publikacji. Scenariusz terapeuta -> publikacja -> pacjent -> cofniecie
na prawdziwym backendzie wymaga osobnego etapu: odizolowanej bazy,
kont testowych, deterministycznego seedowania i czyszczenia danych.
Nie kieruj takich przyszlych testow na normalna baze deweloperska.

Playwright nie zapisuje screenshotow, wideo ani trace. Raporty i coverage sa
ignorowane przez Git. W CI raporty z tych syntetycznych testow sa przechowywane
przez 7 dni. Nie wprowadzaj rzeczywistych danych pacjentow do fixture'ow.

## CI

Istniejacy workflow `frontend-ci.yml` wykonuje lint, TypeScript, testy z coverage,
build i testy przegladarkowe. Nie potrzebuje sekretow ani backendu.
Zmiana samego workflow rowniez uruchamia walidacje PR.

## Dalsze rozszerzanie starszych funkcji

1. Dokumentacja, cwiczenia, plany i wideo.
2. Rezerwacje, kalendarz i powiadomienia.
3. Marketplace, konta, pacjenci, wspolpraca i mapy ciala.
4. Pelne E2E na odizolowanym backendzie i bazie.

Kazdy obszar moze miec osobny PR testowy do juz scalonej implementacji.
Nie zmieniamy starych commitow. Nowe funkcje i naprawy powinny otrzymywac
testy w tym samym PR. Preferuj asercje dotyczace zachowania, a nie struktury
HTML, nazw klas CSS lub duzych snapshotow.

Dokumentacja narzedzi:

- [Testing Library](https://testing-library.com/docs/react-testing-library/intro/)
- [Vitest](https://vitest.dev/guide/)
- [MSW](https://mswjs.io/docs/)
- [Playwright webServer](https://playwright.dev/docs/test-webserver)
