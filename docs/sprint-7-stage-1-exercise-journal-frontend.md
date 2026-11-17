# Sprint 7, etap 1 - frontend dziennika cwiczen

## Wymagania

Backend z kontraktem `sprint-7-stage-1-exercise-journal-api.md` oraz zastosowana
migracja `20261116205703_AddExerciseJournal`. Frontend nie wprowadza migracji
ani zmian backendu. Konto musi miec role Patient i potwierdzony adres e-mail.

## Sciezka pacjenta

1. Otworz Moje plany, a nastepnie udostepniony plan z przynajmniej jednym cwiczeniem.
2. Przy cwiczeniu wybierz Zapisz wykonanie.
3. Podaj date, status i opcjonalnie rzeczywista dawke, ocene bolu oraz komentarz.
4. Zapisz wpis. Parametry zalecone i wykonane sa rozdzielone.
5. Wybierz Historie realizacji planu lub zakladke Moj dziennik.
6. W dzienniku mozna filtrowac daty i status, edytowac i usuwac wpisy.
7. Historia tego planu na kafelku ogranicza liste do wszystkich zapisanych wersji
   wskazanego planu. Pokaz wszystkie plany usuwa to ograniczenie.

Nie pobieramy aktualnego planu, zeby wyswietlic lub edytowac historyczny wpis.
Historia pozostaje kontu autora po cofnieciu publikacji i zakonczeniu wspolpracy.
Widok terapeuty, podsumowania postepow i czat nie naleza do tego etapu.

## Zasady zapisu

- Brak wpisu nie oznacza pominietego cwiczenia.
- Kilka zamierzonych wykonan dziennie to osobne wpisy.
- Bol bez oceny jest null; zero jest jawnym zgloszeniem braku bolu.
- Skipped czysci serie, powtorzenia i czas, ale pozostawia ocene i komentarz.
- Powtorzenia i czas sa alternatywnymi miarami. Wpisanie jednej czysci druga.
- Daty wykonania sa datami kalendarzowymi w Europe/Warsaw, nie UTC.
- Minimalna data jest dniem publikacji w Warszawie, a maksymalna dniem dzisiejszym.
- Komentarz jest ograniczony do 2000 jednostek UTF-16 przed przycieciem.

Nowe zgloszenie otrzymuje requestId dopiero przy pierwszym poprawnym wyslaniu.
Po niepewnym wyniku zachowujemy identyczny klucz i body w pamieci formularza.
Pola sa zablokowane; przycisk Ponow identyczne zgloszenie nie tworzy nowego klucza.
Nie ponawiamy automatycznie. Po konflikcie dodawania wymagamy sprawdzenia
aktualnego udostepnienia. Zmiana publikacji blokuje ponowienie nowego zapisu
z formularza starego planu. Przed zamknieciem niepewnego zapisu sprawdz historie,
zanim potraktujesz to samo wykonanie jako nowe.

Po konflikcie edycji pobierz aktualny wpis, porownaj obie tresci i jawnie wybierz
te do dalszej edycji. Wybor nie zapisuje; wymagane jest osobne Zapisz zmiany.
Po konflikcie usuwania trzeba pobrac swiezy wpis i ponownie potwierdzic usuniecie.
Nie przywracamy wpisow odrzuconych kodem journal_entry_deleted.

## Prywatnosc i nawigacja

Wpisy i formularze nie trafiaja do localStorage, sessionStorage, trwalego cache,
logow ani telemetryki. Teksty sa renderowane bez HTML.
Stan portalu jest ograniczony kluczem konta; wylogowanie usuwa widok.
W trakcie edycji wstrzymujemy odswiezanie planu/dziennika w tle, aby odswiezenie
nie odmontowalo formularza. Operacja zapisu nadal jest weryfikowana przez serwer.
Zamkniecie formularza i przejscia przyciskami portalu wymagaja potwierdzenia
odrzucenia niezapisanych zmian; odswiezenie karty korzysta z beforeunload.
Wysylanie blokuje te przejscia do zakonczenia operacji.

## Testy reczne

1. Dodaj wykonanie Partial z seriami i powtorzeniami, bolem 0 i komentarzem.
2. Sprawdz wpis oraz rozroznienie dawki zaleconej i faktycznej.
3. Dodaj drugie wykonanie tego samego cwiczenia tego samego dnia.
4. Dodaj Skipped bez dawki. Sprawdz, ze ocena bolu moze zostac pusta.
5. Przetestuj date przed publikacja, date przyszla i niepoprawne liczby.
6. Sprawdz filtry, czyszczenie, historie planu i stronicowanie przy >10 wpisach.
7. Edytuj komentarz. Sprawdz, ze kopia zalecen nie zostala zmieniona.
8. Otworz edycje tego samego wpisu w dwoch kartach. Zapisz pierwsza, potem druga.
   Druga powinna zachowac lokalny formularz i poprosic o porownanie.
9. Przed usunieciem zmien ten sam wpis w drugiej karcie. Wymagane jest ponowne
   pobranie i potwierdzenie aktualnej tresci.
10. Cofnij publikacje jako terapeuta. Historia pacjenta nadal dziala, lecz nowy
    wpis ze starego formularza nie moze powstac.
11. Sprawdz zamkniecie zmienionego formularza i Zostan w formularzu.
12. Sprawdz telefon, klawiature i nieujawnianie danych po wylogowaniu.

## Testy automatyczne

```powershell
cd C:\Users\huber\physioflow\frontend
npm run type-check
npm run lint
npm run test:coverage
npm run build
npm run test:browser
```

MSW i testy przegladarkowe uzywaja syntetycznych danych oraz zastepuja API.
Nie sprawdzaja prawdziwej bazy ani backendowej autoryzacji. Testy przegladarkowe
przechodza przez formularz planu, historie, edycje i usuwanie na desktopie
i telefonie. Rzeczywista integracje z API trzeba sprawdzic powyzszymi krokami.
