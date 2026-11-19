# Sprint 8, etap 2: analityka terapeuty

- Moj gabinet (`/dashboard`): najpierw najblizsze wizyty i skroty, potem pacjenci oraz statystyki. Liczniki obejmuja aktywne karty (stan obecny) oraz wszystkie wizyty w wybranym okresie z podzialem wedlug aktualnych statusow.
- Karta aktywnego, powiazanego pacjenta: srednie dzienne ocen bolu z dziennika cwiczen, wykres i tabela z liczba ocen. Backend dodatkowo weryfikuje dostep do dziennika.
- Domyslny tydzien ustala API. Miesiac jest wyznaczany w Europe/Warsaw. Zakres wlasny wymaga obu dat i obejmuje do 366 dni wlacznie.
- Dane pochodza z agregatow serwera. Zero jest ocena; null i dni bez ocen przerywaja linie. Tabela zawiera wylacznie punkty zwrocone przez serwer.
- Dane sa przechowywane tylko w zamontowanym widoku. Zmiana zakresu, konta lub karty porzuca poprzedni odczyt. Odswiezenie po fokusie i co 30 s na widocznej stronie ponownie sprawdza dostep. Odpowiedz 401/403/404 usuwa wyniki.

## Test reczny

1. Na dashboardzie porownaj aktualne liczniki z kalendarzem. Zmien tydzien na miesiac i wlasny zakres; liczba aktywnych kart nie powinna zalezec od dat.
2. Sprobuj podac jedna date, odwrocic zakres lub przekroczyc 366 dni. Przycisk zastosowania powinien pozostac zablokowany.
3. Na powiazanej karcie sprawdz ocene 0, dzien bez ocen i pojedynczy pomiar. Porownaj wykres i tabele; dni bez pomiaru nie powinny sie laczyc.
4. Wybierz pusty okres. Sprawdz osobno komunikat o braku ocen i komunikat o utracie dostepu po zakonczeniu wspolpracy i powrocie fokusu.
5. Powtorz na waskim ekranie. Sprawdz, czy daty, kafelki, wykres i tabela mieszcza sie bez poziomego przewijania strony.

Testy Vitest i Playwright uzywaja syntetycznych danych. Backend i migracje pozostaja bez zmian.
