# Sprint 5.5, etap 3 — opinie, oceny i moderacja

Kontrakt backendu do integracji frontendu. `therapistId` oznacza ID profilu terapeuty.
Daty są w UTC. Statusy JSON mają wartości `Pending`, `Approved`, `Rejected`.
Endpointy opinii zwracają `Cache-Control: no-store`.

## Kwalifikacja pacjenta

Operacje pacjenta wymagają tokenu z rolą `Patient` i aktywnego konta z
potwierdzonym e-mailem. Backend ponownie sprawdza te warunki w Identity.
Właściciel opinii pochodzi z sesji. Dodatkowe pola `patientUserId`, `status`
i `therapistId` w body nie zmieniają właściciela, statusu ani profilu z URL.

Utworzenie i edycja wymagają co najmniej jednej nieusuniętej wizyty `Completed`
o zgodnych `TherapistId` i `PatientUserId`.
Historyczny właściciel wizyty pochodzi z istniejącego modułu rezerwacji i powiązań.
Zakończenie współpracy oraz późniejsze powiązanie karty z innym kontem zachowują
kwalifikację przy pierwotnym pacjencie. Wizyty `Scheduled`, `Cancelled`, `NoShow`,
samo powiązanie z kartą i zaakceptowana współpraca nie uprawniają do opinii.

Unikalny indeks PostgreSQL wymusza jedną opinię na parę konto pacjenta–profil,
także w statusie oczekującym lub odrzuconym. Usunięcie pozwala uprawnionemu
pacjentowi ponownie utworzyć opinię.

## Operacje pacjenta

Wspólna ścieżka to `/api/patient-accounts/me/therapist-reviews/{therapistId}`.

- `GET` zwraca `200` z `ReviewEligibilityDto`.
- `POST` tworzy opinię i zwraca `201`, `Location` oraz `OwnTherapistReviewDto`.
- `PUT` edytuje własną opinię i zwraca `200` z `OwnTherapistReviewDto`.
- `DELETE` usuwa własną opinię i zwraca `204`, także przy ponownym usunięciu.

Body utworzenia i edycji ma ten sam format.

```json
{ "rating": 4, "comment": "Sprawna organizacja wizyty." }
```

`rating` jest wymaganym integerem od 1 do 5. `comment` jest opcjonalny, maksymalnie
1000 znaków przed przycięciem. Brak, `null` i białe znaki są zapisywane jako `null`.
API odrzuca rozpoznawalne e-maile, numery telefonu, adresy WWW i długie ciągi cyfr.
Moderator sprawdza pozostałe dane kontaktowe i szczegółową dokumentację medyczną
przed zatwierdzeniem. Formularz powinien informować o zakazie zamieszczania tych
danych. Komentarze należy renderować jako zwykły tekst.

Przykład odczytu własnej opinii i kwalifikacji.

```json
{
  "canCreate": false,
  "reasonCode": "review_already_exists",
  "review": {
    "id": "00000000-0000-0000-0000-000000000001",
    "therapistId": "00000000-0000-0000-0000-000000000002",
    "rating": 4,
    "comment": "Sprawna organizacja wizyty.",
    "status": "Pending",
    "createdAt": "2026-11-13T12:00:00Z",
    "updatedAt": null,
    "decidedAt": null,
    "rejectionReason": null,
    "version": "00000000-0000-0000-0000-000000000003"
  }
}
```

`canCreate` informuje o możliwości utworzenia nowej opinii. `review` jest `null`,
gdy pacjent jej jeszcze nie ma. Wtedy `reasonCode` jest `null` przy spełnionej
kwalifikacji lub `review_completed_visit_required` bez odbytej wizyty. Istniejąca
opinia daje `review_already_exists`. Pacjent z odbytą wizytą może zarządzać własną
opinią także po ukryciu profilu. Publiczna lista pozostaje wtedy niedostępna.

Każde utworzenie i każda edycja zapisują `Pending`, czyszczą `decidedAt` oraz
`rejectionReason` i zmieniają `version`. Edycja zatwierdzonej opinii od razu usuwa
ją z publicznych odpowiedzi i średniej. Poprawienie odrzuconej opinii ponownie
kieruje ją do moderacji. Zakończenie współpracy zachowuje zatwierdzoną opinię.

## Publiczna lista i podsumowania

`GET /api/therapists/{therapistId}/reviews?pageNumber=1&pageSize=20` działa anonimowo.
`pageNumber` wynosi co najmniej 1, `pageSize` od 1 do 50. Przepełnienie offsetu
daje `400`. Sortowanie jest po utworzeniu malejąco i ID rosnąco przy remisie.

```json
{
  "reviews": {
    "items": [
      {
        "rating": 4,
        "comment": "Sprawna organizacja wizyty.",
        "date": "2026-11-13T12:30:00Z",
        "firstName": "Jan",
        "lastNameInitial": "K."
      }
    ],
    "totalCount": 1,
    "pageNumber": 1,
    "pageSize": 20,
    "totalPages": 1,
    "hasNext": false,
    "hasPrevious": false
  },
  "averageRating": 4,
  "reviewCount": 1
}
```

Publiczne elementy zawierają wyłącznie `rating`, `comment`, `date`, `firstName`,
`lastNameInitial`. `date` to data ostatniej aktualizacji rekordu, a przy jej braku
data utworzenia. Aktualizacja obejmuje również decyzję moderacyjną. Inicjał jest
pierwszym znakiem tekstowym nazwiska z kropką. Starsze konta bez imienia otrzymują
`firstName: ""`, a bez nazwiska `lastNameInitial: null`.

Średnia i liczba obejmują wszystkie zatwierdzone opinie profilu niezależnie od
strony. Brak zatwierdzonych opinii daje `averageRating: null` i `reviewCount: 0`.
Pusta strona poza zakresem zachowuje podsumowanie i `totalCount`. Niedostępny,
usunięty, nieaktywny i nieistniejący profil zwraca `404`.

`TherapistCatalogEntryDto` otrzymuje `averageRating` (`double?`) i `reviewCount`
(`int`) w katalogu, szczegółach oraz wewnątrz dostępnego `profile` w ulubionych.
Średnia arytmetyczna jest obliczana z aktualnych rekordów `Approved` przy odczycie.
Frontend może formatować ją do jednego miejsca po przecinku.

## Moderacja i nadawanie uprawnień

Przyjęty sposób nadawania uprawnień wykorzystuje istniejącą rolę Identity `Admin`.
Członkostwo jest sprawdzane w bazie na każde żądanie. Nadanie i odebranie działa
także dla wcześniej wydanych tokenów. Rola jest tworzona przez istniejący seed.
Administrator wdrożenia nadaje ją wybranemu aktywnemu kontu przez serwerowy
`UserManager<ApplicationUser>.AddToRoleAsync(user, "Admin")`, a odbiera przez
`RemoveFromRoleAsync(user, "Admin")`. Należy sprawdzić `IdentityResult.Succeeded`.
Rejestracja i formularze kont nie nadają uprawnienia moderacyjnego.

`GET /api/moderation/therapist-reviews?pageNumber=1&pageSize=20` domyślnie pobiera
kolejkę `Pending`. `status=Approved` pozwala odnaleźć opublikowaną opinię do
wycofania, a `status=Rejected` odrzucone opinie. Strony mają rozmiar 1–50,
sortowanie po utworzeniu rosnąco, potem ID rosnąco.

Odpowiedź to standardowy `PaginatedList<ModerationReviewDto>`. Element ma pola
`id`, `therapistId`, `rating`, `comment`, `status`, `createdAt`, `updatedAt`,
`decidedAt`, `rejectionReason`, `version`. Kolejka pomija opinie o własnym profilu
moderatora i opinie jego autorstwa. Bezpośrednia próba ich moderowania daje `403`.

- `POST /api/moderation/therapist-reviews/{id}/approve` zmienia `Pending` na `Approved`.
- `POST /api/moderation/therapist-reviews/{id}/reject` zmienia `Pending` na `Rejected`.
- `POST /api/moderation/therapist-reviews/{id}/withdraw` zmienia `Approved` na `Rejected`.

Body zawiera wersję z aktualnego `ModerationReviewDto`. Odrzucenie i wycofanie
wymagają dodatkowo niepustego uzasadnienia do 1000 znaków.

```json
{
  "version": "00000000-0000-0000-0000-000000000003",
  "reason": "Usuń dane kontaktowe i prześlij opinię ponownie."
}
```

Zatwierdzenie wymaga wcześniejszego przeczytania komentarza i sprawdzenia zakazu
danych kontaktowych oraz szczegółowej dokumentacji medycznej przez moderatora.
API obsługuje decyzję moderatora. Automatyczne reguły tekstowe wyłapują podstawowe
dane kontaktowe. Zatwierdzenie przyjmuje samo `version`, a `reason` jest ignorowane.
Sukces zwraca `200` z aktualnym DTO. Każda decyzja zmienia wersję. Frontend po
`409` powinien pobrać aktualną treść przed kolejną decyzją.

Historia `review_status_changes` zapisuje ID opinii, ID aktora, akcję, poprzedni
i nowy status oraz czas UTC w tej samej transakcji co zapis/usunięcie opinii.
Pozostaje po usunięciu opinii. Nie zawiera komentarza ani uzasadnienia.
Uzasadnienie otrzymują autor i moderatorzy poprzez prywatne DTO.

## Błędy i limity

Odpowiedzi błędów mają `application/problem+json`. Frontend rozpoznaje `code`.
Walidacja ma `code: "validation_failed"`, dotychczasowe `errors` i dodatkowe
`errorCodes`, z nazwami pól jako kluczami i tablicami kodów jako wartościami.

```json
{
  "title": "Validation failed",
  "status": 400,
  "code": "validation_failed",
  "errors": { "rating": ["Wartość musi mieścić się w zakresie 1–5."] },
  "errorCodes": { "rating": ["review_rating_invalid"] }
}
```

Komunikaty `errors` mogą zależeć od kultury serwera. Kody są stabilne.

- `400` walidacja pól daje `review_rating_invalid`, `review_comment_too_long`,
  `review_contact_details`, `review_page_invalid`, `review_status_invalid`,
  `review_version_required`, `review_reason_required`, `review_reason_too_long`.
  Błędy formatu JSON, ułamkowe oceny i nieprawidłowe typy parametrów mają
  `validation_failed` z systemowymi `errors`.
- `401` oznacza brak ważnej sesji. Istniejący JWT odrzuca także token pacjenta po
  cofnięciu potwierdzenia e-maila lub dezaktywacji konta.
- `403` niewłaściwa rola blokuje operacje pacjenta. Kontrola aplikacyjna może dać
  `email_confirmation_required`, `review_moderation_forbidden` lub
  `review_self_moderation_forbidden`.
- `404` oznacza brak własnej opinii przy edycji, brak opinii przy moderacji lub
  niedostępny profil przy odczycie publicznym.
- `409` daje `review_already_exists`, `review_completed_visit_required`,
  `review_status_conflict` lub `concurrent_modification`.
  Równoczesne utworzenia tej samej pary mają jeden sukces `201` i jeden konflikt.
- `429` daje `rate_limit_exceeded`, `Retry-After` i `Cache-Control: no-store`.

Utworzenie i edycja współdzielą limit 5 żądań na 15 minut na konto pacjenta.
Ustawienia to `RateLimiting:TherapistReviewWrite:PermitLimit` i `WindowMinutes`.
Publiczne listy używają istniejącego limitu `PublicTherapistCatalog` na IP.

## Migracja i weryfikacja

Migracja `AddTherapistReviews` tworzy `therapist_reviews` i `review_status_changes`,
klucze obce, unikalny indeks pary, indeksy listowania oraz ograniczenia oceny 1–5
i statusu 1–3. Wersja opinii jest tokenem optymistycznej współbieżności.
Istniejące profile otrzymują przy odczycie `reviewCount: 0` i `averageRating: null`.

Z katalogu `backend` migrację wdraża standardowe polecenie projektu.

```powershell
dotnet ef database update --project PhysioFlow.Infrastructure --startup-project PhysioFlow.Api
```

Testy HTTP obejmują kwalifikację po wizycie, izolację dwóch kont, uprawnienia,
ponowną moderację po edycji, wycofanie, prywatność DTO, paginację i limit żądań.
Testy PostgreSQL wymuszają równoczesny zapis, sprawdzają atomowość historii,
konflikt edycji i zatwierdzania, migrację oraz translację zapytań publicznych.
Backend CI ustawia `PHYSIOFLOW_TEST_POSTGRES` i uruchamia także te testy.
