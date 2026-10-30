# UAI.ZeroVP – ukázkové API pro výuku Dockeru

Jednoduché REST API pro správu úkolů (ToDo), rozdělené do tří projektů.
Slouží jako výchozí bod pro navazující lekce o kontejnerizaci.

## Struktura repozitáře

```text
UAI.ZeroVP.sln                 solution – zůstává v kořeni
src/                           zdrojový kód aplikace
├─ UAI.ZeroVP.DTO/             kontrakt API
├─ UAI.ZeroVP.Data/            entita a paměťové úložiště
└─ UAI.ZeroVP.API/             webové API, kontrolery, mapování
case-study-a-dockerfile/       výuka – vrstvení Dockerfile
case-study-b-tooling/          výuka – nástroje a workflow
case-study-c-configuration/    výuka – konfigurace aplikace
```

Aplikace i výukové materiály sdílejí jeden kontext, proto se **sestavuje vždy
z kořene repozitáře** – jak `dotnet build`, tak `docker build`. Podrobnosti
v sekci [Výukové materiály k Dockeru](#výukové-materiály-k-dockeru).

| Projekt | Typ | Obsah | Závislosti |
|---|---|---|---|
| `UAI.ZeroVP.DTO` | Class library | Kontrakt API – `TodoItemDto`, `CreateTodoItemRequest`, `CreateWeeklyTodoItemsRequest`, `UpdateTodoItemRequest`, `InstanceInfoDto`, výčet `TodoPriority` | žádné |
| `UAI.ZeroVP.Data` | Class library | Entita `TodoItem`, rozhraní `ITodoRepository`, paměťová implementace `InMemoryTodoRepository`, registrace do DI | `DTO` |
| `UAI.ZeroVP.API` | ASP.NET Core Web API | Kontrolery, mapování entita ↔ DTO, konfigurace pipeline | `DTO`, `Data` |

Závislosti tvoří jednosměrný řetěz **DTO → Data → API**. Kontroler zná jen rozhraní
`ITodoRepository`, takže výměna paměťového úložiště za databázi se ho nedotkne.

## Úložiště dat

Data žijí v paměťovém úložišti procesu (`ConcurrentDictionary`), které je při běžném chodu aplikace doplněno o perzistenci do souboru JSON (`JsonPersistence`). Při startu aplikace se úkoly načtou ze souboru a při ukončení se do něj automaticky uloží (`TodoPersistenceHostedService`).

### Konfigurace perzistence (JsonPersistence)

Ukládání a načítání z disku se konfiguruje v sekci `JsonPersistence`:

```jsonc
// appsettings.json
"JsonPersistence": {
  "Enabled": true,
  "FilePath": "data/todos.json"
}
```

- `Enabled` (`bool`, výchozí `true`) – povoluje nebo zakazuje diskovou perzistenci.
- `FilePath` (`string`, výchozí `data/todos.json`) – relativní nebo absolutní cesta k souboru s úkoly.

Nastavení lze přepsat proměnnými prostředí (oddělovač sekcí `__`):
```bash
JsonPersistence__Enabled=true
JsonPersistence__FilePath=data/todos.json
```

### Chování při startu a ukázková data (Seeding)

Při startu aplikace bez existujícího souboru se vkládají čtyři ukázkové úkoly (pokud je `Todo:SeedSampleData` zapnuto). Pokud soubor s perzistovanými úkoly existuje, data ze souboru mají přednost a ukázková data se přepíší obsahem souboru.

Ukázková data lze vypnout nastavením:

```jsonc
// appsettings.json
"Todo": { "SeedSampleData": false }
```

nebo proměnnou prostředí `Todo__SeedSampleData=false`.

## Spuštění

```bash
dotnet run --project src/UAI.ZeroVP.API
```

API naslouchá na `http://localhost:5258`. Kořen `/` přesměruje na Swagger UI
(nebo na seznam úkolů, pokud je Swagger vypnutý).

Port žádná část kódu nepředepisuje – určuje ho konfigurace (viz [Více instancí na localhostu](#více-instancí-na-localhostu)).

## Endpointy

| Metoda | Cesta | Popis |
|---|---|---|
| `GET` | `/api/todos` | Seznam úkolů; volitelné filtry `?isCompleted=true\|false` a `?userId=anna` |
| `GET` | `/api/todos/{id}` | Detail úkolu |
| `POST` | `/api/todos` | Založení úkolu → `201 Created` + hlavička `Location` |
| `POST` | `/api/todos/repeat` | Creates a weekly sequence of the same task (`repeatCount` + 1 tasks, seven days apart) → `201 Created` + list of tasks, see [docs/weekly-recurring-todos.md](docs/weekly-recurring-todos.md) |
| `PUT` | `/api/todos/{id}` | Úplná aktualizace úkolu |
| `POST` | `/api/todos/{id}/complete` | Označení úkolu za dokončený |
| `DELETE` | `/api/todos/{id}` | Smazání úkolu → `204 No Content` |
| `GET` | `/api/info` | Hostname instance, prostředí, doba běhu, počet úkolů v paměti |
| `GET` | `/health` | Kontrola zdraví – použije ji `healthcheck` v docker-compose.yml |
| `GET` | `/swagger` | Interaktivní dokumentace Swagger UI |
| `GET` | `/openapi/v1.json` | Popis API ve formátu OpenAPI |

Chybové odpovědi mají jednotný tvar `ProblemDetails` (RFC 9457).
Připravené požadavky k odzkoušení jsou v souboru `src/UAI.ZeroVP.API/UAI.ZeroVP.API.http`.

## Více instancí na localhostu

Vedle sebe může běžet několik instancí API. Každá musí mít **vlastní port**, dá se rozlišit
podle **ID instance** a zapisuje do **vlastního log souboru**.

> **Známé omezení (do sloučení části 2 issue 0VP-T4-09).** Úložiště je pořád jen v paměti
> procesu, takže instance **nesdílejí data**: úkol vytvořený na jedné instanci druhá nevidí.
> Sdílení dat řeší navazující sub-issue (perzistentní úložiště, 0VP-T4-08).

### Spuštění skriptem

```bash
# Linux / macOS / Git Bash: 3 instance na portech 5258, 5259, 5260
bash scripts/run-instances.sh 3 5258
```

```powershell
# Windows (PowerShell): totéž
powershell -ExecutionPolicy Bypass -File scripts/run-instances.ps1 -Count 3 -BasePort 5258
```

Skript projekt jednou sestaví, spustí `N` procesů na po sobě jdoucích portech a ukončíte je všechny
pomocí Ctrl+C. Každé instanci nastaví port (`--urls`), ID `api-1`, `api-2`, … (`Instance__Id`) a vlastní
log soubor (`FileLogging__IncludeInstanceId=true`). Volitelné parametry:

| Nastavení | `run-instances.sh` | `run-instances.ps1` | Výchozí |
|---|---|---|---|
| Počet instancí | 1. argument | `-Count` | `3` |
| Port první instance | 2. argument | `-BasePort` | `5258` |
| Předpona ID instance | `ID_PREFIX=node` | `-IdPrefix node` | `api` |
| Prostředí | `ASPNETCORE_ENVIRONMENT=Production` | `-EnvironmentName Production` | `Development` |
| Bez sestavení | `NO_BUILD=1` | `-NoBuild` | sestaví se |

### Ruční spuštění

Port se předává přepínačem `--urls` (nebo proměnnou `ASPNETCORE_URLS`, nebo klíčem `urls` v konfiguraci).
Každou instanci spusťte v samostatném terminálu:

```bash
dotnet run --project src/UAI.ZeroVP.API --no-launch-profile --urls http://localhost:5258
dotnet run --project src/UAI.ZeroVP.API --no-launch-profile --urls http://localhost:5259
```

Přepínač `--no-launch-profile` vynechá `launchSettings.json`, který by jinak nastavil prostředí
`Development`. Druhou instanci spouštějte až po dokončení sestavení první, jinak se obě sestavení
potkají na stejných souborech (skript sestaví jednou a spouští už hotové binárky).

Při ručním spuštění nastavte každé instanci ještě vlastní log soubor, jinak píšou obě do téhož:

```bash
Instance__Id=api-2 FileLogging__IncludeInstanceId=true \
  dotnet run --project src/UAI.ZeroVP.API --no-launch-profile --urls http://localhost:5259
```

### Jak instance rozeznat

1. **`GET /api/info`** – každá instance vrací vlastní `instanceId`, `urls` (adresy, na kterých opravdu
   naslouchá) a `port`. Původní pole (`machineName`, `environment`, `version`, `utcNow`, `uptime`,
   `todoCount`) zůstávají beze změny.

   ```bash
   curl http://localhost:5258/api/info   # "instanceId": "api-1", "port": 5258, ...
   curl http://localhost:5259/api/info   # "instanceId": "api-2", "port": 5259, ...
   ```

2. **Log soubory** – `src/UAI.ZeroVP.API/logs/zerovp-api-1-yyyyMMdd.log`, `zerovp-api-2-yyyyMMdd.log`, …
3. **Konzole** – událost `ApplicationStarted` uvádí adresu, na které instance naslouchá.

### ID instance a port – nastavení

| Co | Jak se nastaví | Výchozí hodnota |
|---|---|---|
| Port / adresa | `--urls`, `ASPNETCORE_URLS`, klíč `urls` (dále `ASPNETCORE_HTTP_PORTS`, `Kestrel:Endpoints`) | `http://localhost:5258` při `dotnet run` s `launchSettings.json`, jinak výchozí port Kestrelu `5000` |
| ID instance | `Instance__Id` (v `appsettings.json` `Instance:Id`) | `{MachineName}-{port}`, např. `PC-5258` (při portu `0` místo portu ID procesu) |
| ID instance v názvu log souboru | `FileLogging__IncludeInstanceId=true` | `false` – název je jako dřív, `zerovp-yyyyMMdd.log` |

Zapnutá volba změní název na `zerovp-{instanceId}-yyyyMMdd.log`. Znaky nevhodné do názvu souboru
(lomítka, dvojtečky, mezery) se v ID nahradí podtržítkem. Retence (`RetainedFileCount`) maže jen soubory
**vlastní instance**, logy ostatních instancí ve stejné složce nechá být.

Jedna instance s výchozím nastavením se chová přesně jako dřív: stejný port, stejný název logu.

## Logování

Aplikace zapisuje do trvalého logu start a zastavení, každý HTTP požadavek a chyby – vždy s časem,
identifikací události, výsledkem a případným popisem chyby. Záznamy jsou JSON Lines v denních souborech
`logs/zerovp-yyyyMMdd.log` (při souběžném běhu více instancí `zerovp-{instanceId}-yyyyMMdd.log`,
viz [Více instancí na localhostu](#více-instancí-na-localhostu)). Podrobnosti, katalog událostí a příklady jsou v [docs/logging.md](docs/logging.md).

## Swagger

Dokument OpenAPI generuje vestavěná knihovna .NET 10 (`Microsoft.AspNetCore.OpenApi`)
a vystavuje ho na `/openapi/v1.json`. Balíček `Swashbuckle.AspNetCore.SwaggerUI`
nad ním přidává jen interaktivní uživatelské rozhraní na `/swagger` – druhý generátor
dokumentu se tedy nepoužívá.

Popisy operací a vlastností ve Swaggeru pocházejí z XML komentářů v kódu;
proto mají všechny tři projekty zapnuté `<GenerateDocumentationFile>`.

Swagger je zapnutý v prostředí Development a vypnutý v Production.
Přepnout ho lze nastavením nebo proměnnou prostředí:

```jsonc
// appsettings.json
"Swagger": { "Enabled": true }
```

```bash
# například při spuštění kontejneru
ASPNETCORE_ENVIRONMENT=Production Swagger__Enabled=true dotnet UAI.ZeroVP.API.dll
```

### Co OpenAPI dokumentace obsahuje

U každého endpointu popisuje dokument cestu a HTTP metodu, vstupní parametry
s datovými typy (v cestě, v query řetězci i v těle požadavku) a všechny
odpovědi s JSON schématem:

| Kód | Význam | Schéma odpovědi | Kde |
|---|---|---|---|
| `200` / `201` / `204` | úspěch | `TodoItemDto`, seznam `TodoItemDto`, `InstanceInfoDto`, nebo bez těla u `204` | podle endpointu |
| `400` | neplatný vstup (validace těla, nebo špatná hodnota filtru `isCompleted`) | `ValidationProblemDetails` | `POST`, `PUT`, `GET /api/todos`, `POST /api/todos/repeat` |
| `404` | úkol se zadaným `id` neexistuje | `ProblemDetails` | endpointy s `{id}` |
| `500` | neočekávaná chyba serveru | `ProblemDetails` | všechny endpointy |

Priorita úkolu se v požadavcích i odpovědích posílá jako řetězec
(`Low`, `Normal`, `High`). Schémata mají ukázkové hodnoty, které Swagger UI
předvyplní v **Example Value** a při **Try it out**.

Úkol může mít volitelný `userId` (libovolný řetězec do 200 znaků, např. jméno nebo e-mail).
Zatím slouží jen jako štítek a neověřuje se; počítá se s ním pro pozdější přihlašování.
Filtr `?userId=` porovnává hodnotu bez ohledu na velikost písmen; prázdná hodnota
`?userId=` nevrátí žádný úkol. U `PUT` se vynechané `userId` ignoruje a u úkolu
zůstane původní hodnota; změní se jen tehdy, když klient pošle novou.

Dokumentaci otevřete takto:

1. Spusťte aplikaci: `dotnet run --project src/UAI.ZeroVP.API`.
2. Interaktivní stránka je na `http://localhost:5258/swagger`.
3. Strojově čitelný dokument (OpenAPI 3.1) je na `http://localhost:5258/openapi/v1.json`.

Mimo prostředí Development je Swagger ve výchozím stavu vypnutý (viz výše).

### Rychlý test

```bash
curl http://localhost:5258/api/todos
curl -X POST http://localhost:5258/api/todos \
  -H "Content-Type: application/json" \
  -d '{"title":"Napsat Dockerfile","priority":"High"}'
curl "http://localhost:5258/api/todos?userId=anna"
curl -X POST http://localhost:5258/api/todos -H "Content-Type: application/json" -d '{"title":"Úkol pro Annu","userId":"anna"}'
curl http://localhost:5258/api/info
```

## Bezpečnostní skenování

Repozitář má automatickou bezpečnostní kontrolu (zranitelné balíčky, statická analýza, detekce secretů). Lokálně ji spustíš příkazem `powershell -ExecutionPolicy Bypass -File scripts/security-scan.ps1`, v GitHub Actions běží jako job `security-scan`. Podrobnosti jsou v [docs/ci-pipeline.md](docs/ci-pipeline.md).

## Poznámka ke kódování

Zdrojové soubory jsou uloženy jako **UTF-8 s BOM**. Bez BOM dekóduje kompilátor C#
na Windows zdrojový kód systémovou ANSI kódovou stránkou a diakritika v řetězcích
se rozbije. Pravidlo hlídá `.editorconfig` (`charset = utf-8-bom`).

## Výukové materiály k Dockeru

Nad touto aplikací jsou postavené tři nezávislé case studies, každá o pěti příkladech.
Každý příklad má zadání pro studenta a komentované řešení. Náročnost stoupá
od case study A k case study C.

| Case study | Úroveň | Téma |
|---|---|---|
| [A – vrstvení Dockerfile](case-study-a-dockerfile/) | začátečníci | jeden Dockerfile, který se postupně zlepšuje: od 1,34 GB k zpevněnému image za 352 MB |
| [B – nástroje a workflow](case-study-b-tooling/) | mírně pokročilí | životní cyklus, diagnostika rozbitých kontejnerů, vrstvy, registry |
| [C – konfigurace aplikace](case-study-c-configuration/) | pokročilí | jeden image, mnoho prostředí: proměnné, soubory, priorita zdrojů, tajemství |

Materiály case studies jsou psané **anglicky**; samotná aplikace a tento soubor zůstávají v češtině.

Každá case study končí příkladem s Docker Compose. Všechny tři jsou samostatné –
lze je probírat v libovolném pořadí nebo jen některý.

### Společné pro všechny case studies

Sestavuje se vždy **z kořene projektu**, Dockerfile se uvádí přepínačem `-f`:

```bash
docker build -f case-study-a-dockerfile/02-multi-stage-build/Dockerfile -t zerovp:a02 .
```

Ignorovací soubory jsou uložené vedle jednotlivých Dockerfile jako
`<jméno Dockerfile>.dockerignore`, protože všechny příklady sdílejí jeden kontext.
V běžném projektu patří `.dockerignore` do kořene repozitáře.

## Spuštění v Dockeru (Docker Compose)

API lze spustit v izolovaném Docker kontejneru pomocí Docker Compose. Soubory
`Dockerfile`, `.dockerignore` a `docker-compose.yml` jsou v kořeni repozitáře.

> **Známé omezení.** Aplikace zatím používá in-memory úložiště. Data vytvořená
> v kontejneru se při jeho restartu nebo zastavení ztratí. Persistentní úložiště
> bude doplněno v navazujícím issue.

Kontejner je jen jedna instance API. GitHub Actions kontejnery nesestavují ani
netestují, CI běží bez Dockeru.

### Příkazy

```bash
# sestavení image a spuštění na pozadí
docker compose up --build -d

# stav kontejneru (po startu by měl být "healthy")
docker compose ps

# logy aplikace (sledování průběhu: -f)
docker compose logs api
docker compose logs -f api

# zastavení a odstranění kontejneru
docker compose down
```

Po restartu kontejneru (`docker compose restart`) i po `docker compose down` se
data v paměti ztratí a znovu se vloží ukázkové úkoly.

### Kontrola, že kontejner běží

```bash
curl http://localhost:5258/health
curl http://localhost:5258/api/info
```

Swagger UI je na `http://localhost:5258/swagger`.

### Konfigurace

Nastavení se předává výhradně proměnnými prostředí v `docker-compose.yml`:

| Proměnná | Význam |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | prostředí ASP.NET Core |
| `Swagger__Enabled` | zapnutí Swagger UI |
| `Todo__SeedSampleData` | vložení ukázkových úkolů při startu |
| `FileLogging__Directory` | složka souborových logů v kontejneru (`/app/logs`) |

Aplikace v kontejneru běží pod uživatelem bez práv roota a naslouchá na portu
`8080`, který je na hostiteli dostupný jako `5258`. Kontrola zdraví volá `/health`
z malého pomocného nástroje v image (runtime image nemá `curl`).