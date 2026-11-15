# Sprint 6 — wideo w ćwiczeniach i planach terapii

Backend obsługuje jedno opcjonalne źródło wideo na ćwiczenie. Terapeuta przesyła własny plik lub podaje adres YouTube. Instrukcja tekstowa pozostaje wymagana przy tworzeniu i edycji ćwiczenia. Uprawnienia do biblioteki i planów opisuje [kontrakt terapii](stage-2-therapy-api.md).

## Uwierzytelnianie i dostęp

Opisane tutaj endpointy biblioteki i odtwarzania terapeuty wymagają JWT z rolą `Therapist`. Właściciel jest ustalany z sesji. Obce ćwiczenia i materiały zwracają 404, konto pacjenta i sama rola administratora zwracają 403, brak sesji zwraca 401. Samo powiązanie konta z kartą nie udostępnia wideo. [Etap 3](stage-3-patient-sharing-api.md) dodaje osobne odtwarzanie pacjenta ograniczone do pozycji jawnie udostępnionej wersji planu, bez dostępu do biblioteki.

Serwer sprawdza właściciela przed przesłaniem obiektu i przed wygenerowaniem linku do odtwarzania. Klient przekazuje identyfikator materiału otrzymany z biblioteki, pozycji planu lub historii. Klucze obiektów, nazwy bucketów i dane dostępu do R2 nie występują w tych kontraktach.

Każda zmiana materiału wymaga aktualnego `version` ćwiczenia i zwraca całe `ExerciseDto` z nowym tokenem. Nieaktualny token daje 409 z `code = concurrent_modification`. Brak lub pusty UUID daje 400. Token jest wspólny dla edycji tekstu, archiwizacji i zmiany wideo.

## Endpointy

- `POST /api/exercises/{exerciseId}/video/upload` przyjmuje `multipart/form-data` z jednym polem `file` i polem `version`. Dodaje albo zastępuje materiał. Zwraca 200 i `ExerciseDto`.
- `PUT /api/exercises/{exerciseId}/video/youtube` przyjmuje JSON `{ "version": "UUID", "url": "https://youtu.be/dQw4w9WgXcQ" }`. Dodaje albo zastępuje materiał. Zwraca 200 i `ExerciseDto`.
- `DELETE /api/exercises/{exerciseId}/video` przyjmuje JSON `{ "version": "UUID" }` w body. Odłącza materiał od biblioteki. Zwraca 200 i `ExerciseDto` z `video = null`. Można wywołać także dla ćwiczenia bez filmu z aktualnym tokenem.
- `GET /api/exercise-videos/{videoId}/playback` zwraca 200 i `{ "url": "https://...podpisany-adres...", "expiresAt": "...UTC..." }` dla własnego przesłanego nagrania. Link jest ważny przez 300 sekund. Dla materiału YouTube zwraca 409 z `code = video_not_uploaded`.

Ćwiczenie należy najpierw utworzyć dotychczasowym `POST /api/exercises`, a następnie dodać wideo. Zmiana źródła pomiędzy plikiem a YouTube korzysta z tych samych endpointów i zastępuje pojedynczą referencję.

Odpowiedzi mają `Cache-Control: no-store`. Błąd walidacji zwraca 400 z `code = validation_failed` i błędami pól `file`, `url` lub `version`. Brak konfiguracji R2 lub błąd magazynu daje 503 z `code = storage_unavailable`. Nieudane przesłanie zachowuje dotychczasową referencję. Współbieżne zapisy są rozstrzygane tokenem optymistycznej współbieżności w transakcji bazy.

## Walidacja nagrań

Limit pojedynczego pliku wynosi 104857600 bajtów (100 MiB). Plik musi zawierać co najmniej jeden bajt i spełniać walidację nagłówka kontenera. Dozwolone rozszerzenia i odpowiadające im typy MIME to `.mp4` z `video/mp4`, `.webm` z `video/webm` oraz `.mov` z `video/quicktime`. Wielkość liter rozszerzenia i typu MIME nie ma znaczenia. Serwer nadaje własną nazwę obiektu i nie wykorzystuje nazwy pliku klienta do budowania ścieżki.

Walidacja obejmuje rozszerzenie, MIME, rzeczywistą liczbę bajtów, zgodność z deklarowaną długością i nagłówek kontenera. MP4 i MOV wymagają pola `ftyp` z obsługiwaną marką kontenera. WebM wymaga nagłówka EBML i oznaczenia DocType `webm`. Backend buforuje plik na dysku tymczasowym z limitem, a po operacji usuwa bufor. Walidacja nagłówka nie dekoduje całego filmu. Zgodność kodeka z przeglądarką trzeba sprawdzić po stronie odtwarzacza, np. MP4 z H.264/AAC.

Limit całego żądania wynosi 105906176 bajtów (101 MiB), uwzględniając narzut multipart. Przekroczenie limitu serwera lub proxy może zwrócić 413. Reverse proxy, IIS lub hosting muszą dopuszczać żądania tej wielkości i mieć odpowiedni czas na przesłanie. Katalog tymczasowy procesu musi mieć uprawnienia zapisu i miejsce na równoległe uploady.

## YouTube

Obsługiwane są adresy HTTP i HTTPS z identyfikatorem filmu złożonym z dokładnie 11 znaków `A–Z`, `a–z`, cyfr, `_` lub `-`.

- `youtube.com/watch?v=ID` oraz hosty `www.youtube.com`, `m.youtube.com` i `music.youtube.com`.
- `/shorts/ID`, `/embed/ID` i `/live/ID` na tych samych hostach.
- `youtu.be/ID`.
- `/embed/ID` na `youtube-nocookie.com` i `www.youtube-nocookie.com`.

Parametry śledzenia, czas startu, lista odtwarzania i fragment są pomijane. Wynik ma postać `https://www.youtube.com/watch?v=ID`. Adres zawierający kilka parametrów `v`, dane logowania, niestandardowy port, obcy host albo samą playlistę jest odrzucany. Limit długości adresu wynosi 2048 znaków.

Walidacja jest lokalna. Serwer nie wykonuje żądań do wskazanego URL, nie pobiera filmu ani metadanych i nie podąża za przekierowaniami. Dostępność filmu, uprawnienia do oglądania i możliwość osadzania zależą od YouTube. Frontend może z otrzymanego identyfikatora w kanonicznym URL zbudować adres oficjalnego odtwarzacza osadzanego.

## Kontrakt biblioteki i planów

`ExerciseDto` oraz `TherapyPlanItemDto` otrzymują opcjonalne pole `video`. To samo pole występuje w `content.exercises[]` historii wersji. Wartość `null` oznacza brak wideo.

Przesłane nagranie ma następujący kształt.

```json
{
  "id": "d78d2306-7367-4f75-991b-70621a4917aa",
  "source": "Upload",
  "youtubeUrl": null,
  "contentType": "video/mp4",
  "sizeBytes": 1234567
}
```

Materiał YouTube ma następujący kształt.

```json
{
  "id": "0ac5952c-e486-4026-866b-e373f206074c",
  "source": "YouTube",
  "youtubeUrl": "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
  "contentType": null,
  "sizeBytes": null
}
```

Frontend pobiera adres odtwarzania przesłanego nagrania dopiero przed użyciem. Po wygaśnięciu pobiera nowy. Podpisany adres jest tymczasowym uprawnieniem dla jego posiadacza i działa do końca ważności także po odłączeniu materiału z biblioteki. Frontend powinien unikać zapisywania go w historii nawigacji, logach i trwałym stanie. Historię planu odtwarza przez `video.id` zapisany w danej wersji.

Tworzenie nowej pozycji planu kopiuje referencję do materiału bieżącego w bibliotece podczas tworzenia tej pozycji. Zachowanie `id` pozycji w zapisie lub korekcie zachowuje także jej materiał. Aby utworzyć pozycję z bieżącą wersją ćwiczenia i jego filmu, klient pomija `id` pozycji i przekazuje `exerciseId`. Pola `video`, `videoId`, `storageKey` i tekst kopii pozycji są kontrolowane przez serwer i nie podlegają edycji w żądaniu planu.

Dodanie filmu do ćwiczenia nie uzupełnia wcześniej zapisanych pozycji. Zastąpienie, odłączenie i archiwizacja ćwiczenia nie zmieniają filmów w istniejących pozycjach i historii. Edycja tekstu dotychczasowym endpointem ćwiczenia zachowuje materiał. Istniejące ćwiczenia i plany bez wideo działają z `video = null`. Starszy JSON historii bez pola `Video` odczytywany jest jako brak materiału.

## R2 i wdrożenie

1. Utwórz osobny bucket dla prywatnych nagrań. Wyłącz dostęp przez `r2.dev` i publiczne domeny niestandardowe. Bucket nagrań musi mieć nazwę różną od `R2:BucketName`, używanego przez istniejącą obsługę zdjęć.
2. Utwórz token R2 z uprawnieniami odczytu i zapisu obiektów dla wymaganych bucketów. Dane dostępowe ustaw w sekretach środowiska.
3. Ustaw `R2__AccountId`, `R2__AccessKey`, `R2__SecretKey` oraz `R2__VideoBucketName`. Dotychczasowy `R2__BucketName` obsługuje zdjęcia. Przykłady nazw zawiera `backend/PhysioFlow.Api/appsettings.Development.example.json`. Brak osobnego bucketa lub użycie nazwy bucketa zdjęć uruchamia bezpieczny wariant zwracający 503 dla operacji magazynu wideo. YouTube i ćwiczenia bez wideo działają dalej.
4. Skonfiguruj CORS bucketa dla rzeczywistych originów frontendu. Wystarczą metody `GET` i `HEAD`, nagłówek `Range` oraz ujawnienie `Content-Length`, `Content-Range`, `Accept-Ranges`, `Content-Type` i `ETag`. Przesyłanie odbywa się przez API backendu.
5. Zachowaj obiekty nagrań. Usuń reguły lifecycle automatycznie usuwające nagrania, ponieważ ich identyfikatory mogą istnieć tylko w historycznym JSON planu.
6. Uruchom migrację z katalogu repozytorium.

```powershell
dotnet ef database update --project backend/PhysioFlow.Infrastructure --startup-project backend/PhysioFlow.Api
```

Migracja `20261115142243_AddExerciseVideoMaterials` dodaje tabelę materiałów, opcjonalne klucze obce biblioteki i pozycji planów, ograniczenie pojedynczego źródła oraz trigger blokujący modyfikowanie i usuwanie materiałów. Rozpoznane starsze linki biblioteki z `youtube_url` są importowane jako materiały YouTube. Import obejmuje `watch?v=ID` z `v` jako pierwszym parametrem, linki skrócone, shorts, live i embed. Inne stare wartości pozostają w dotychczasowej kolumnie i nie są eksponowane jako wideo. Migracja zachowuje treść istniejących planów i ich historii.

Ta wersja zachowuje wszystkie przesłane obiekty i rekordy materiałów również po odłączeniu. Dotyczy to także obiektu przesłanego przed nieudanym lub niejednoznacznie zakończonym zapisem bazy. Zapobiega to usunięciu nagrania użytego w zapisanym planie, wersji historycznej lub równoczesnej operacji. Ewentualne późniejsze porządkowanie musi uwzględniać bibliotekę, wszystkie pozycje, historyczny JSON oraz trwające operacje. Trigger blokuje fizyczne usuwanie materiałów.

Podpisany URL używa endpointu S3 konta R2 i operacji GET, z ważnością 5 minut. Konfigurację podpisywania i wymagane flagi SDK opisują [dokumentacja podpisanych URL](https://developers.cloudflare.com/r2/api/s3/presigned-urls/) oraz [przykład AWS SDK dla .NET](https://developers.cloudflare.com/r2/examples/aws/aws-sdk-net/).

## Weryfikacja

Testy obejmują przesyłanie, podmianę, zmianę źródła, odłączenie, wymagane instrukcje, limit pliku i rzeczywistą długość strumienia, rozszerzenie/MIME/nagłówek, adresy YouTube, brak dostępu do cudzych nagrań, maskowanie błędów dostawcy, krótką ważność linków, materiały wyłącznie w historii oraz zgodność dotychczasowej biblioteki i planów. Testy PostgreSQL sprawdzają również migrację starszych danych i JSON, niezmienność materiałów w EF i SQL oraz atomowość równoczesnej podmiany filmu.

```powershell
$env:PHYSIOFLOW_TEST_POSTGRES = 'Host=localhost;Port=5432;Database=postgres;Username=physioflow;Password=dev'
dotnet test backend/PhysioFlow.sln
```

Testy tworzą i usuwają własne losowe bazy. Konto testowe wymaga `CREATEDB`. Testy magazynu używają atrapy R2 i mocka SDK. Przed uruchomieniem w środowisku docelowym należy sprawdzić rzeczywisty upload, odtwarzanie, CORS, prywatność bucketa i limity hostingu przy użyciu skonfigurowanego konta Cloudflare.
