# Specyfikacja funkcjonalna

## Autentykacja

- rejestracja przez e-mail (terapeuta)
- zaproszenie pacjenta przez link w e-mailu
- logowanie, wylogowanie
- refresh token (rotacja przy każdym refresh)
- reset hasła przez e-mail
- 2FA (planowane — TOTP)

## Zarządzanie pacjentami (terapeuta)

- lista pacjentów z wyszukiwaniem i filtrowaniem (imię, nazwisko, e-mail, telefon)
- paginacja po stronie serwera
- karta pacjenta: dane osobowe, PESEL, data urodzenia, kontakt, adres
- historia schorzeń i leków
- notatki ogólne
- archiwizacja pacjenta (soft delete)
- eksport karty do PDF

## Kalendarz i wizyty

- widok dzienny i tygodniowy (React Big Calendar)
- definiowanie dostępności terapeuty (slot'y per dzień tygodnia + override'y na konkretne daty)
- czas trwania wizyty: 30 / 45 / 60 minut
- tworzenie, edycja, usuwanie wizyty
- blokada podwójnych rezerwacji (walidacja w CreateAppointmentCommand)
- przypisanie pacjenta do wizyty
- status wizyty: zaplanowana / odbyta / odwołana / nieobecność
- bufor między wizytami
- przypomnienia e-mail (Resend, uruchamiane z background joba)
- rezerwacja online przez portal pacjenta
- odwołanie wizyty przez pacjenta z portalu

## Body Mapping

- sylwetka ciała SVG (przód / tył)
- zaznaczanie obszarów bólowych kliknięciem na Konva Stage
- kolory: czerwony (ból ostry), pomarańczowy (chroniczny), żółty (napięcie)
- notatka tekstowa przy każdym punkcie
- zapis stanu mapy jako relacja rodzic-dziecko (BodyMap → PainPoint[])
- koordynaty znormalizowane (0.0–1.0) niezależne od rozmiaru SVG
- porównanie map z różnych wizyt
- eksport mapy do PNG

## Dokumentacja medyczna (EDM)

- formularz wywiadu wstępnego (dynamiczny, JSON w DB)
- notatka po wizycie — rich text przez Tiptap (HTML + plain text do search)
- ocena bólu w skali 1–10
- załączniki: zdjęcia, PDF, RTG (Cloudflare R2)
- historia wszystkich wpisów chronologicznie

## Biblioteka ćwiczeń

- dodawanie ćwiczenia: nazwa, opis, zdjęcia, link YouTube
- prywatna biblioteka terapeuty (izolacja po `TherapistId`)
- wyszukiwanie i filtrowanie po grupie mięśniowej (enum `MuscleGroup`)

## Plan terapii

- tworzenie planu dla pacjenta
- wybór ćwiczeń z biblioteki
- parametry: serie, powtórzenia, czas, przerwy, kolejność, notatki
- publikacja planu — dopiero po publikacji widoczny w portalu pacjenta

## Portal pacjenta

- widok nadchodzących wizyt
- aktualny plan ćwiczeń z opisami i wideo
- dzienniczek: poziom bólu (1–10), samopoczucie (1–10), wykonane ćwiczenia
- czat z terapeutą (SignalR ChatHub)
- odwołanie wizyty

## Analityka (dashboard terapeuty)

- liczba aktywnych pacjentów
- liczba wizyt w tygodniu
- wykres poziomu bólu pacjenta w czasie (Recharts LineChart)

## Planowane rozszerzenia

- płatności online przy rezerwacji — Stripe lub Przelewy24 (BLIK)
- wideo-konsultacje (WebRTC)
- eksport danych pacjenta na żądanie (RODO)
