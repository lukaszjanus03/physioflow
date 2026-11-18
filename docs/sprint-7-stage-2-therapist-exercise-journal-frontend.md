# Sprint 7, etap 2 - frontend podgladu dziennika terapeuty

## Zakres

- Ekran `/patients/{patientId}/exercise-journal` w panelu terapeuty.
- Wejscie z modulu Dziennik cwiczen w karcie pacjenta.
- Wejscie Dziennik tego planu z edytora planu z zachowanym filtrem planu.
- Tylko odczyt: lista, podsumowanie i osobno pobierane szczegoly.
- Filtry dat wykonania, statusu, planu i pozycji zapisanej w historycznej wersji.
- Stronicowanie, stale sortowanie backendu i grupowanie po dniu wykonania.
- Osobne prezentowanie dawki zaleconej i faktycznie zgloszonej.
- Rozroznienie bolu null i 0 oraz dat wykonania, publikacji, utworzenia i edycji.

Frontend uzywa istniejacego kontraktu
`docs/sprint-7-stage-2-therapist-exercise-journal-api.md`.
Nie dodaje migracji, operacji zapisu, interpretacji medycznej ani nowych zaleznosci.
Nie wylicza procentu realizacji ani nie zalicza brakujacych wpisow jako pominiec.

## Filtry i podsumowanie

Daty i status stosuje sie przyciskiem Zastosuj. Zmiana filtrow zeruje strone.
Przyszla granica dat jest dozwolona. Niepoprawne daty, status i UUID sa
odrzucane przed wykonaniem zapytan.

Filtr planu wybiera sie z planu albo przyciskiem Tylko ten plan przy wpisie.
Tylko ta pozycja wybiera identyfikator historycznej pozycji wraz z planem;
nie jest to identyfikator cwiczenia bibliotecznego. Przyciski usuwania aktywnych
filtrow oraz Wyczysc pozwalaja powrocic do szerszego zakresu.
Nie pobieramy aktualnego planu ani biblioteki do wyswietlenia starych zgloszen.

Lista i podsumowanie otrzymuja identyczne filtry. Paginacja trafia tylko na liste.
Liczniki pochodza z API podsumowania, a nie z elementow aktualnej strony.
Ostatnie wykonanie bierze date z API; null jest prezentowane jako Brak wykonan,
rowniez przy dodatniej liczbie pominiec.

Oba wyniki sa pobierane razem. Jesli suma licznikow nie zgadza sie z totalCount,
frontend odswieza pare jeden raz. Przy ponownej rozbieznosci ukrywa kafelki
podsumowania i proponuje kolejne, jawne odswiezenie. Nie tworzy petli ponowien.
Porownanie licznikow nie oznacza atomowego snapshotu: API ma osobne odczyty
i zmiany zachowujace liczbe wpisow moga nie zostac wykryte tym porownaniem.

## Dostep i prywatnosc

Nowy klient API ma tylko metody GET. Token version z DTO nie jest wykorzystywany
do modyfikowania wpisu. Nie ma przyciskow edycji ani usuwania.
Serwer pozostaje ostatecznym zrodlem decyzji o uprawnieniach.

Dane kliniczne sa przechowywane tylko w stanie zamontowanego widoku,
bez QueryClient cache, localStorage, sessionStorage i telemetryki.
Zmiana konta lub karty usuwa poprzedni widok i anuluje zadania.
Odczyty odswiezaja sie na fokusie i co 30 sekund dla widocznej karty.
Nie jest to nowa funkcja SignalR ani gwarancja natychmiastowego cofniecia tresci
juz dostarczonej do przegladarki.

Layout terapeuty czeka na odczyt sesji w przegladarce przed przekierowaniem.
Zapobiega to powrotowi przez strone glowna na dashboard podczas odswiezania
glebokiego adresu, przy zachowaniu blokady kont bez roli Therapist.

401, 403 lub 404 z listy, podsumowania lub szczegolow usuwa caly podglad,
w tym otwarte szczegoly. Sama zmiana filtrow nie resetuje odmowy dostepu.
Przycisk Sprawdz dostep i pobierz od nowa wykonuje swieze odczyty.
Nie probujemy odzyskac historii przez pacjenckie endpointy lub inny plan.
Nie wyswietlamy surowego detail z odpowiedzi ani nie rozrozniamy powodow 404.
Teksty sa wyswietlane bez interpretowania HTML.

## Testy reczne z backendem

1. Pacjent z aktywna wspolpraca i aktualnym powiazaniem dodaje wpisy
   Completed, Partial i Skipped, w tym bol 0 i brak oceny.
2. Terapeuta otwiera karte tego pacjenta, a nastepnie Dziennik cwiczen.
3. Sprawdz kopie zaleconej dawki, zgloszone parametry, komentarz oraz szczegoly.
   Nie powinno byc przyciskow zmiany wpisu.
4. Sprawdz daty, status, Tylko ten plan i Tylko ta pozycja. Po zmianie filtrow
   odswiezaja sie lista i podsumowanie. Filtry dotycza daty wykonania.
5. Przy >10 wpisach sprawdz kolejne strony. Liczniki obejmuja caly zakres.
6. Dla samych Skipped ostatnie wykonanie powinno byc puste, mimo liczby pominiec.
7. Otworz plan i wybierz Dziennik tego planu. Nie zgub niezapisanych zmian planu:
   dotychczasowy mechanizm potwierdzenia nadal powinien dzialac.
8. Pacjent edytuje lub usuwa wpis. Odswiez dziennik terapeuty i sprawdz liczniki.
9. Cofnij publikacje lub zarchiwizuj plan bez konczenia wspolpracy. Historia
   obecnego okresu powiazania pozostaje dostepna.
10. Zakoncz wspolprace albo zarchiwizuj karte. Po kolejnym odczycie podglad
    i szczegoly znikaja. Zmiana filtrow nie przywraca tresci.
11. Po ponownym powiazaniu konta nie oczekuj wpisow z poprzedniego okresu.
    Autor nadal ma swoja historie w panelu pacjenta, na zasadach etapu 1.
12. Sprawdz telefon, klawiature, Escape w szczegolach i zmiane pacjenta/konta.

## Testy automatyczne

```powershell
cd C:\Users\huber\physioflow\frontend
npm run type-check
npm run lint
npm run test:coverage
npm run build
npm run test:browser
```

Testy komponentow i klienta HTTP: `therapist-journal*.test.*`.
Testy przegladarkowe: `therapist-journal.browser.spec.ts`, desktop i telefon.
Sprawdzaja zachowanie interfejsu z symulowanym API. Nie zastepuja testow
backendowej autoryzacji, bazy ani recznej integracji z rzeczywistym serwerem.
