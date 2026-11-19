# Sprint 9: opcjonalne 2FA we frontendzie

API i migracja: [kontrakt backendu](sprint-9-optional-two-factor-api.md).
Frontend generuje QR lokalnie i nie wysyla sekretu do zewnetrznego dostawcy.
Wyzwanie, klucz konfiguracji, kod i kody odzyskiwania nie trafiaja do trwalego
storage, adresu strony ani cache zapytan. Zmiany zabezpieczen koncza sesje.
Kody odzyskiwania sa wyswietlane przed ponownym logowaniem i nie sa odtwarzane
po odswiezeniu strony.

## Test reczny na lokalnym API

1. Uruchom API po migracji oraz frontend. Uzyj kont testowych i aplikacji
   uwierzytelniajacej zgodnej z TOTP.
2. Zaloguj dotychczasowe konto bez 2FA. Powinno wejsc do panelu bez kodu.
3. Na nowym koncie po potwierdzeniu e-maila sprawdz ekran propozycji.
   Wybierz Przypomnij przy nastepnym logowaniu, wyloguj sie i zaloguj ponownie:
   propozycja wraca. Frontend nie udostepnia trwalego pominiecia propozycji.
   Konta, ktore wczesniej pominely propozycje trwale, nadal moga wlaczyc 2FA
   w ustawieniach.
   Niepotwierdzony terapeuta moze skonfigurowac 2FA recznie w ustawieniach;
   backend nie wyswietla mu propozycji.
4. Pacjent: Moje konto > Bezpieczenstwo konta. Terapeuta: Ustawienia >
   Bezpieczenstwo konta. Wybierz Zarzadzaj 2FA, a nastepnie Wlacz 2FA.
   Zeskanuj QR lub wprowadz klucz recznie. Mozna anulowac bez wlaczania.
5. Potwierdz aktualnym kodem. Zapisz fikcyjne kody odzyskiwania i wroc do logowania.
   Kod uzyty do wlaczenia nie jest ponownie akceptowany: zaczekaj na nowy.
6. Podaj prawidlowe haslo. Panel nie jest jeszcze dostepny; wpisz kod TOTP.
   Sprawdz bledny kod, poprawny kod i osobno logowanie kodem odzyskiwania.
   Ten sam kod odzyskiwania drugi raz nie powinien zadzialac.
7. W ustawieniach zregeneruj kody, a osobno wylacz 2FA. Kazda operacja
   wymaga aktualnego hasla i dowodu 2FA oraz powrotu do logowania po sukcesie.
8. Sprawdz klawiature i telefon. Nie zapisuj zrzutow z rzeczywistymi sekretami.

Playwright i Vitest korzystaja z fikcyjnych danych i symulowanego API.
Testy przegladarkowe sprawdzaja lokalne renderowanie QR, ale nie skanowanie
telefonem ani rzeczywista weryfikacje TOTP przez backend.
