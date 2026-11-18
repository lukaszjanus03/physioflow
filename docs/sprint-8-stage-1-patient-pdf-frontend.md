# Sprint 8, etap 1: rozszerzony eksport PDF karty pacjenta

## Zakres

Istniejacy PatientCardPdf nadal generuje plik lokalnie w przegladarce.
Nie korzystamy z niewdrozonego endpointu export-pdf i nie zmieniamy backendu.
Opcjonalna dokumentacja pochodzi z kontraktu
`sprint-8-stage-1-medical-record-export-api.md`.

Karta pacjenta > Eksport PDF otwiera konfiguracje:

1. Domyslnie sama karta: dane osobowe, kontakt, historia medyczna, leki i notatki.
2. Opcjonalnie Dolacz dokumentacje wizyt i daty od/do. Puste obie daty oznaczaja
   cala historie; mozna podac tylko jedna granice. Minimum 1900-01-01.
3. Przygotuj eksport pobiera wszystkie strony dokumentacji i aktualna karte.
4. Podsumowanie pokazuje liczbe wpisow. Pusty zakres jest komunikowany przed
   pobraniem; po potwierdzeniu PDF zawiera karte i informacje o braku wpisow.
5. Pobierz PDF ponownie sprawdza dostep do karty i generuje plik. Przygotuj
   ponownie odswieza dane, a zmiana opcji usuwa poprzednie przygotowanie.

## Pobieranie i ograniczenia

- API dokumentacji jest odpytywane kolejno od strony 1, pageSize=100,
  ze stalymi filtrami, az hasNext=false. Kolejnosc wpisow pozostaje serwerowa.
- Blad dowolnej strony przerywa przygotowanie; nie generujemy czesciowego PDF.
- Zmieniona liczba wynikow, duplikat ID lub niespojna paginacja wymagaja ponowienia.
- API nie zapewnia transakcyjnej migawki pomiedzy stronami. Niezauwazalnych
  zmian przy niezmienionej liczbie wpisow frontend nie moze wykluczyc.
  Interfejs i PDF wyraznie informuja o braku wspolnej migawki.
- Daty filtru dotycza poczatku wizyt w Europe/Warsaw, nie czasu zatwierdzenia.
  Wpis bez wizyty jest mozliwy jedynie bez obu filtrow, zgodnie z backendem.
- PDF przedstawia wersje, date zatwierdzenia i date wizyty w Europe/Warsaw.
  Przy braku wizyty nie zastepuje jej data zatwierdzenia.
- Kazdy wpis rozpoczyna sie na nowej stronie. Sekcje kliniczne sa rozdzielone,
  podsumowanie dla pacjenta ma osobna etykiete i nie jest automatycznie publikowane.
- Zamykanie okna anuluje zadania HTTP. Sam renderer PDF nie obsluguje przerwania
  obliczen; po ich zakonczeniu sprawdzamy anulowanie i konto przed pobraniem.
- Tresci nie trafiaja do dodatkowego trwalego cache ani pamieci przegladarki.
  Przygotowane dane sa usuwane przy zmianie opcji, zamknieciu lub utracie dostepu.
- Tymczasowy adres Blob jest zwalniany po pobraniu zgodnie z dotychczasowym
  opoznieniem 60 s, pozwalajac przegladarce zakonczyc odczyt.

## Test reczny

1. Zaloguj terapeute, otworz wlasna karte i Eksport PDF. Zamknij okno:
   zadne generowanie nie powinno zostac uruchomione.
2. Bez zaznaczania dokumentacji wybierz Przygotuj eksport i Pobierz PDF.
   Sprawdz dotychczasowe sekcje karty, polskie znaki, naglowek i stopke.
3. Zaznacz dokumentacje, pozostaw daty puste i przygotuj. Liczba wpisow
   powinna obejmowac wszystkie zatwierdzone wpisy, a nie tylko widoczna strone listy.
4. Pobierz plik. Sprawdz daty wizyt, wersje, daty zatwierdzenia i osobne sekcje
   wywiadu, oceny, dzialan, zalecen oraz opcjonalnego podsumowania.
5. Utworz szkic albo pozostaw niezatwierdzone zmiany. Nie powinny znalezc sie w PDF.
   Zatwierdz korekte, przygotuj eksport ponownie i sprawdz nowy numer wersji.
6. Sprawdz daty od/do (wlacznie), jedna granice, puste obie granice i od>do.
   Nieprawidlowy zakres blokuje przygotowanie bez zadania API.
7. Wybierz zakres bez wpisow. Przed pobraniem pojawia sie wyrazna informacja.
8. Sprawdz dlugie tresci, wielolinijkowe akapity oraz wielostronicowy wpis:
   nic nie powinno znikac ani nachodzic na naglowek lub stopke.
9. Przerwij siec podczas pobierania albo zamknij okno podczas generowania.
   Nie moze zostac pobrany niepelny lub anulowany plik.
10. Sprawdz wlasna karte archiwalna oraz brak uprawnien do obcej/usunietej karty.
11. Sprawdz okno na telefonie: przewijanie, daty i przyciski bez poziomego scrolla.

## Automatyzacja

W katalogu frontend:

```powershell
npm run type-check
npm run lint
npm test
npm run test:coverage
npm run build
npm run test:browser
```

Testy API uzywaja MSW, komponenty kontrolowanego renderera, a Playwright pobiera
rzeczywiste PDF-y z React PDF przy syntetycznych odpowiedziach API.
Testowe pliki znajduja sie w ignorowanym test-results; nie sa danymi pacjentow.
Autoryzacje i wybor zatwierdzonych wersji potwierdzaja osobno testy backendu.
