# Rewizja protokołu, asymetria wysiłku i rewizja hipotez

Tekst do przeniesienia do arkusza informacyjnego, rozdziału metodycznego, rozdziału wyników i artykułu. Spisany
7.11.2026 na podstawie niezależnego przeglądu (AB-V5-REVIEW-PL.md) i archiwów pomiarów. Wersja angielska i źródło
faktów: `thesis/results/AB-COMPARISON-v1.md`, rozdział 13. Część 3 (rewizja hipotez) jest propozycją, która wymaga
zgody obu autorów i promotora przed wpisaniem do dokumentów.

## 1. Historia protokołu (akapit do rozdziału metodycznego i arkusza informacyjnego)

Plan badań z kwietnia 2026 roku zakładał wcześniejszą rejestrację hipotez H1, H2 i H3. Przed wykonaniem pomiarów,
których wyniki raportujemy, protokół zmieniono w kilku miejscach. Każda zmiana wynikała z ograniczeń technicznych
albo z braku możliwości zweryfikowania pierwotnego sformułowania, a nie z uzyskanych wyników. Zestawienie zmian
podaje tabela.

| Data | Element | Plan z kwietnia 2026 | Stan obecny | Powód |
|---|---|---|---|---|
| czerwiec 2026 (początek implementacji) | przedmiot badania | system do zarządzania magazynem i przepływem pracy | forum dyskusyjne według wspólnej specyfikacji | obaj autorzy rozwijali działające forum; wspólna specyfikacja jednej istniejącej domeny łatwiej zapewnia równoważność funkcjonalną niż dwa nowe systemy |
| 17.10.2026 | liczba powtórzeń | co najmniej 30 na scenariusz | co najmniej 10 ważnych przebiegów demo na stronę, co najmniej 3 dla stress, capacity i wstrzykiwania awarii | jeden przebieg demo trwa około 12 minut razem z odtworzeniem danych; dokładny test Manna-Whitneya z korektą Holma dla 18 metryk osiąga p = 0,003 już przy 8 przebiegach na stronę, więc 10 daje zapas |
| 17.10.2026 | izolacja przebiegów | pełny restart systemu między architekturami | odtworzenie danych i nowe pody przed każdym przebiegiem, druga architektura wyskalowana do zera, kolejność ABBA | obie architektury działają na jednym klastrze minikube; ABBA równoważy dryf w czasie, a odtworzenie danych daje każdemu przebiegowi ten sam zbiór |
| 17.10.2026 | sprzęt | Ryzen 9 7900X, 64 GB, ograniczenie łącza do 100 Mb/s | laptop z WSL2 (8 rdzeni, 12 GB) jako replikacja oraz host Linux (16 rdzeni, 28 GB) dla tabeli głównej, bez kształtowania ruchu | sprzęt faktycznie dostępny; ingress działa na tym samym hoście co generator obciążenia |
| 25.10.2026 | definicja H2 | sufit współbieżności porównywalny lub wyższy, bez definicji kolana | otwarty model częstości przybyć, kolano jako pierwszy krok, w którym 95. percentyl podróży rdzeniowych przekracza 500 ms, błędy przekraczają 1 % albo osiągnięta częstość spada poniżej 95 % zadanej | pierwotne sformułowanie nie dawało się sprawdzić |
| 25.10.2026 | metryki H3 | zależności, podatności, czas potoku CI/CD | zależności, podatności, złożoność cyklomatyczna, lokalna pętla deweloperska | potoki CI obu systemów działają na różnych platformach (GitLab, GitHub) i różnych maszynach, więc ich czasy nie są porównywalne |
| 7.11.2026 | zakres H1 | serwer i aplikacja desktopowa | wyłącznie serwer (propozycja, część 3) | proces WebView aplikacji desktopowej nie został zmierzony po żadnej stronie, a serwer dołączony do aplikacji desktopowej B działa na magazynie danych w pamięci, więc obie aplikacje desktopowe nie są równoważne |
| 7.11.2026 | metryki H3 | złożoność i pętla deweloperska | zależności, podatności, rozmiar kodu i złożoność cyklomatyczna mierzona jednym narzędziem dla wszystkich języków (propozycja, część 3) | czasy pętli deweloperskiej zależą od pamięci podręcznych i sieci, a narzędzia B nie są zainstalowane na maszynie pomiarowej; analizy SonarQube nie wykonano |
| 7.11.2026 | reguły ważności przebiegu | kontrola obciążenia procesora przez generator | dodatkowo kontrola pamięci hosta, zgodności uruchomionego obrazu z oznaczoną wersją kodu i pełnej konfiguracji B | na laptopie przebiegi capacity korzystały z pamięci wymiany, a mimo to przechodziły wszystkie reguły; żadna reguła nie wiązała obrazu z tagiem |

## 2. Asymetria wysiłku optymalizacyjnego (do podrozdziału o zagrożeniach dla trafności wewnętrznej)

Po pierwszych pomiarach architektura B przeszła osobny, kilkudniowy etap optymalizacji: między wersjami
thesis-v4-b i thesis-v5-b powstało około 50 zmian wydajnościowych, między innymi profilowanie PGO na podstawie
testów, strojenie GOMAXPROCS i GOMEMLIMIT, pamięć podręczna odczytów, wczesne zakończenie wyszukiwania, indeksy
stronicowania z rozstrzyganiem remisów po identyfikatorze, fillfactor 90, wyłączenie fastupdate w indeksie GIN, pule
buforów renderowania, budowa drzewa komentarzy w czasie liniowym, nagłówki ETag i pamięć podręczna instrukcji SQL.
Architektura A otrzymała wyłącznie lustrzane zmiany warstwy danych: tę samą pamięć podręczną odczytów zgodną ze
wspólnym kontraktem, sortowanie wyników wyszukiwania od najnowszych z planami wykonania dobieranymi przy każdym
zapytaniu, strojenie indeksu GIN i fillfactor oraz natychmiastowe wybudzanie przekaźnika outbox. Po stronie A nie
przeprowadzono porównywalnego profilowania środowiska uruchomieniowego .NET. Środowisko .NET 10 domyślnie stosuje
dynamiczne PGO i wielopoziomową kompilację JIT, więc część korzyści, które B uzyskało jawnym profilem, A ma bez
dodatkowej pracy. Wpływ asymetrii widać w historii stosunku zużycia procesora aplikacji na podróż (B do A): 0,96
w rundzie v1, 0,86 w v3, 0,50 w v4 i 0,33 w v5. W tym czasie A utrzymywało się na poziomie 4,8–5,8 ms na podróż, a B
spadło z 4,84 do 1,86 ms. Warstwa danych została wyrównana kontraktem pamięci podręcznej i lustrzanymi zmianami,
warstwa środowiska uruchomieniowego nie. Wyniki należy więc czytać jako porównanie architektury B po dedykowanym
etapie optymalizacji z architekturą A po lustrzanych zmianach warstwy danych.

## 3. Rewizja hipotez (propozycja do akceptacji)

H1 (efektywność zasobowa po stronie serwera). Zunifikowany monolit (B) przy równoważnym obciążeniu zużywa istotnie
mniej pamięci i mniej czasu procesora na obsłużoną podróż użytkownika po stronie serwera oraz dostarcza mniejsze
artefakty wdrożeniowe niż architektura oddzielona (A). Liczba bajtów przesłanych na podróż jest raportowana jako
obserwacja; w rundzie v5 działa ona na niekorzyść B (około 1,95 raza więcej niż A).

H2 (sufit przepustowości). Bez zmiany definicji. W tekście trzeba zapisać, że kolano wyznacza na obu stronach baza
danych z budżetem jednego procesora, więc mechanizmem jest koszt zapytań SQL na podróż, a nie model współbieżności.

H3 (utrzymywalność, metryki statyczne). Architektura B ma mniejszą powierzchnię zależności (liczba zależności
bezpośrednich i przechodnich według plików blokad), mniejszą ekspozycję na znane podatności (liczba podatności HIGH
i CRITICAL z dostępną poprawką w obrazach wdrożeniowych, ta sama wersja i baza skanera Trivy dla obu stron), mniejszy
rozmiar kodu (cloc, bez kodu generowanego, testów i zależności dołączonych do repozytorium) oraz niższą złożoność
cyklomatyczną mierzoną jednym narzędziem dla C#, TypeScript i Go (lizard: suma złożoności, złożoność na 1000
linii kodu, odsetek funkcji o złożoności powyżej 10). Wszystkie cztery metryki są mierzone na oznaczonych wersjach
kodu, są powtarzalne i nie zależą od maszyny. Czasy potoków CI/CD i pętli deweloperskiej pozostają opisem
jakościowym.

## 4. Zdania do poprawienia w obecnych dokumentach (lista z przeglądu z 7.11.2026)

1. Artykuł, rejestr odchyleń: zamiast „17-elementowy rejestr” podać aktualną liczbę pozycji z
   `AB-COMPARISON-v1.md` §6.2; liczbę kontroli wspólnego profilu zaktualizować do wartości z rundy v5.
2. Artykuł: D17 nie wystąpiło w v5 (zero nieudanych otwarć rozmowy w przebiegach A).
3. Artykuł, mediana dostarczenia powiadomień w A: po wybudzaniu przekaźnika wynosi około 41 ms (było 1,5–2,3 s).
4. Artykuł i rozdział wyników, H2: zdanie „sufit B jest niższy, bo baza nasyca się pierwsza” dotyczy rund v1–v4;
   dla v5 przepisać po rundzie na hoście 16-rdzeniowym.
5. Artykuł, ograniczenia: runda końcowa używa stałego pliku PNG dla J4, nie obrazu generowanego dla każdego VU.
6. Rozdział wyników: replikacja v5 jest wykonana na tagu thesis-v5-b i przebudowanym obrazie A (git-1ae3c46).
7. Wszystkie dokumenty: dodać akapit o asymetrii wysiłku (część 2) i historię protokołu (część 1).
8. Arkusz i artykuł: usunąć część desktopową z H1 i zastąpić H3 nowym brzmieniem, jeśli rewizja zostanie
   zaakceptowana.
