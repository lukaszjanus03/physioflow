# Sprint 7, etap 3: prywatny czat - frontend

## Wejscie

- Pacjent: portal > Wiadomosci; dodatkowo Moi terapeuci > Napisz wiadomosc.
- Terapeuta: Wiadomosci w menu; dodatkowo karta pacjenta > Wiadomosci.
- Nowa rozmowa: wybrac aktywna wspolprace i Otworz rozmowe.
- Historia zakonczonej wspolpracy: wybrac istniejaca rozmowe z listy.
- Szybki czat: przycisk Czat w prawym dolnym rogu poza pelna sekcja Wiadomosci.
  Minus/Escape minimalizuje bez usuwania szkicu. X zamyka i wymaga potwierdzenia
  utraty niewyslanej tresci. Strzalka powiekszenia otwiera pelny widok wybranej rozmowy.
  Zmiana strony lub przejscie do pelnego widoku rowniez wymaga odrzucenia szkicu;
  zachowanie szkicu dotyczy minimalizacji na biezacej stronie.
  Na telefonie okno zajmuje prawie caly ekran ponad dolnym menu.

Nowa wspolpraca lub generacja powiazania nie wznawia starej rozmowy.
Nazwy rozmowcow sa mapowane przez cooperationId, nie ID profilu terapeuty.
Gdy nazwa jest niedostepna, widok pokazuje rozroznialny identyfikator rozmowy.

## Zachowanie

- REST zapisuje wiadomosci i odczyty. Wspolny SignalRProvider odbiera wskazowki
  i odswieza dane z REST, bez dopisywania tresci lub licznikow z eventow.
- Handlery huba sa rejestrowane przed startem. Nie tworzymy drugiego huba dla widoku czatu.
- Widok aktywnej rozmowy sprawdza REST co 10 s, globalna lista co 20 s,
  tylko przy widocznej karcie; dodatkowo po fokusie, eventach i reconnect.
- Wyszukiwanie nazw dziala na wszystkich pobranych rozmowach. Starsza historia
  jest doladowywana kursorem before; zalegle wiadomosci kursorem after.
- Kursor synchronizacji wynika z rzeczywiscie pobranej ciaglej sekwencji,
  nie z lastSequence ani numeru nowo potwierdzonej wlasnej wiadomosci.
- Tresc: maksymalnie 4000 jednostek UTF-16 przed trim, bez pustych tresci i NUL.
  Enter dodaje linie, Ctrl+Enter/Meta+Enter wysyla. HTML jest zwyklym tekstem.
- Po niepewnym zapisie tresc i UUID sa zamrozone. Ponowienie uzywa identycznego
  body, rowniez w rozmowie, ktora zdazyla przejsc w readonly. Nie generujemy
  automatycznie nowego klucza. Porzucenie wymaga potwierdzenia.
- 429 respektuje Retry-After; konflikt klucza blokuje ponowienie.
- Odczyt przekazuje ID ostatniej faktycznie widocznej otrzymanej wiadomosci,
  tylko przy widocznym dokumencie z fokusem. Status Przeczytano pochodzi z readAt REST.
- Zminimalizowane okno nie wysyla odczytow. Dymek i pelny widok uzywaja wspolnego
  providera i tego samego huba; na stronie pelnego czatu dymek nie jest montowany.
- Odejscie od niewyslanej tresci wymaga potwierdzenia. Wysylka blokuje odejscie.
- Tresci i szkice pozostaja w pamieci widoku. Nie trafiaja do localStorage,
  sessionStorage ani trwalego cache. Odmontowanie/wylogowanie usuwa je.
- Utrata dostepu 401/403/404 usuwa historie i formularz; ponowna proba jest jawna.
- Nie dodajemy zalacznikow, grup, obecnosci, typing ani powiadomien mailowych czatu.

## Test reczny na prawdziwym backendzie

1. Uruchom backend po migracji czatu oraz frontend. Przygotuj aktywnie powiazane
   konta pacjenta i terapeuty; zaloguj je w dwoch niezaleznych profilach przegladarki.
2. Otworz rozmowe z obu stron. Wyslij po jednej wiadomosci, takze wieloliniowej.
   Bez przeladowania druga strona powinna otrzymac tresc, a licznik odzwierciedlac serwer.
3. Przejdz odbiorca do innej sekcji. Wyslij nowa wiadomosc: licznik wzrasta,
   ale samo pobranie listy nie oznacza odczytania. Po otwarciu widocznej historii
   nadawca otrzymuje Przeczytano. Ukryta karta bez fokusu nie wysyla odczytu.
4. Przy dlugiej historii przewin w gore. Nowe wiadomosci nie powinny wyrzucac
   czytelnika na dol. Doladowanie starszych powinno zachowac pozycje przewijania.
5. Rozlacz siec/hub. Widok zachowuje historie, pokazuje tryb REST i po powrocie
   pobiera zalegle wiadomosci bez duplikatow. Sprawdz tez poczatkowo niedostepny hub.
6. Dla niepewnej wysylki uzyj Ponow te sama wiadomosc: w Network identyczny
   requestId i content, a na serwerze tylko jeden rekord. Nie odswiezaj strony,
   bo szkice i klucze nie sa trwale zapisywane.
7. Wpisz niewyslana tresc i kliknij powrot/menu/wylogowanie: zostan albo
   swiadomie odrzuc zmiany. W trakcie zapisu przyciski nie wysylaja drugi raz.
8. Zakoncz wspolprace lub zarchiwizuj karte. Historia pozostaje dostepna zgodnie
   z kontraktem, a nowa wiadomosc jest zablokowana. Nowa wspolpraca tworzy nowy kontekst.
9. Odbierz dostep zgodnie z kontraktem (np. zmien powiazanie pacjenta).
   Po odpowiedzi 403/404 poprzednie tresci i szkic znikaja z widoku.
10. Sprawdz waski ekran: wybor rozmowy, powrot do listy, formularz bez poziomego scrolla.
11. Poza sekcja Wiadomosci otworz dymek, wybierz rozmowe i wpisz szkic. Zminimalizuj
    i otworz ponownie: tresc pozostaje. Wiadomosc otrzymana w zminimalizowanym oknie
    nie powinna byc oznaczona jako przeczytana do ponownego pokazania jej odbiorcy.
12. X i powiekszenie z niewyslanym szkicem wymagaja potwierdzenia. Po wybraniu
    Zostan w formularzu tresc pozostaje. Powiekszenie bez szkicu otwiera te sama
    rozmowe w pelnym widoku, bez drugiego okna czatu.

## Automatyzacja

W katalogu frontend: npm test, npm run test:coverage, npm run type-check,
npm run lint, npm run build, npm run test:browser (ostatnie po udanym buildzie).

Testy korzystaja z danych syntetycznych. Playwright wymusza REST fallback i
nie potwierdza rzeczywistego transportu SignalR ani backendowej autoryzacji.
Testy SignalR mockuja biblioteke, sprawdzajac kolejnosc rejestracji i cykl zycia.
