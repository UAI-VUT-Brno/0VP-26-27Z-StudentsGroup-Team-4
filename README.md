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
- `Shared` (`bool`, výchozí `false`) – sdílený režim pro více souběžných instancí, viz [Sdílené úložiště](#sdílené-úložiště-více-instancí-jedna-databáze).
- `LockTimeoutMilliseconds` (`int`, výchozí `10000`) – jak dlouho se ve sdíleném režimu čeká na zámek souboru.

Nastavení lze přepsat proměnnými prostředí (oddělovač sekcí `__`):
```bash
JsonPersistence__Enabled=true
JsonPersistence__FilePath=data/todos.json
```

### Sdílené úložiště (více instancí, jedna databáze)

Ve výchozím režimu drží každý proces data v paměti a do souboru je zapíše až při ukončení, takže dvě
instance se stejným souborem by si data přepisovaly. Pro souběžné instance proto existuje **sdílený režim**
(`JsonPersistence__Shared=true`), kde je **soubor jediným zdrojem pravdy**:

- každá operace (čtení i zápis) soubor krátce **zamkne** (soubor `todos.json.lock`; v rámci procesu navíc
  semafor), přečte aktuální obsah, provede změnu a zapíše ji do dočasného souboru, který **atomicky nahradí**
  `todos.json`. Úkol vytvořený jednou instancí je tedy hned vidět všem ostatním;
- současné zápisy z více instancí se **serializují**: nic se neztratí, soubor se nepoškodí a nevznikají
  chyby „soubor je používán jiným procesem“ (zámek se opakuje s krátkou prodlevou, nejdéle
  `LockTimeoutMilliseconds`, pak operace skončí chybou `TimeoutException`);
- při ukončení se nic neukládá, protože se zapisuje po každé změně;
- při startu více instancí najednou vytvoří chybějící soubor s ukázkovými daty jen první; ostatní ho najdou hotový;
- poškozený soubor se zaloguje (`corrupted`), zkopíruje do `todos.json.corrupt-<čas>` a instance začne s prázdným
  stavem; první další zápis soubor opraví.

```bash
JsonPersistence__Enabled=true
JsonPersistence__Shared=true
JsonPersistence__FilePath=/absolutni/cesta/data/todos.json   # u všech instancí stejná, nejlépe absolutní
```

Skripty `scripts/run-instances.ps1` a `.sh` to nastavují samy (soubor `<repo>/data/todos.json`, lze změnit
parametrem `-DataFile` / proměnnou `DATA_FILE`). Formát souboru je stejný jako v režimu bez sdílení.

Omezení: zámek souboru spolehlivě funguje na lokálním disku. Na síťových úložištích (NFS/SMB) a mezi hostitelskými
stroji se na něj spoléhat nelze; pro takové nasazení je potřeba skutečná databáze.

`GET /api/info` ukazuje použité úložiště (bez tajemství):

```json
"storage": { "type": "SharedJsonFile", "persistent": true, "shared": true, "location": "/repo/data/todos.json" }
```

`type` je `InMemory`, `JsonFile` (jedna instance) nebo `SharedJsonFile`. Instance se stejným `location` a `shared: true`
vidí stejná data.

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
| `GET` | `/api/info` | Identita instance (`instanceId`, `urls`, `port`), použité úložiště (`storage`), hostname, prostředí, doba běhu, počet úkolů |
| `GET` | `/health` | Kontrola zdraví – použije ji `healthcheck` v docker-compose.yml |
| `GET` | `/swagger` | Interaktivní dokumentace Swagger UI |
| `GET` | `/openapi/v1.json` | Popis API ve formátu OpenAPI |

Chybové odpovědi mají jednotný tvar `ProblemDetails` (RFC 9457).
Připravené požadavky k odzkoušení jsou v souboru `src/UAI.ZeroVP.API/UAI.ZeroVP.API.http`.

## Více instancí na localhostu

Vedle sebe může běžet několik instancí API. Každá musí mít **vlastní port**, dá se rozlišit
podle **ID instance**, zapisuje do **vlastního log souboru** a všechny **sdílejí jedno úložiště dat**:
úkol vytvořený přes jednu instanci je hned vidět přes kteroukoli jinou
(viz [Sdílené úložiště](#sdílené-úložiště-více-instancí-jedna-databáze)).

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
log soubor (`FileLogging__IncludeInstanceId=true`). Všechny instance dostanou stejný soubor s daty
(`JsonPersistence__Shared=true`, `JsonPersistence__FilePath`). Volitelné parametry:

| Nastavení | `run-instances.sh` | `run-instances.ps1` | Výchozí |
|---|---|---|---|
| Počet instancí | 1. argument | `-Count` | `3` |
| Port první instance | 2. argument | `-BasePort` | `5258` |
| Předpona ID instance | `ID_PREFIX=node` | `-IdPrefix node` | `api` |
| Prostředí | `ASPNETCORE_ENVIRONMENT=Production` | `-EnvironmentName Production` | `Development` |
| Soubor s daty (sdílený) | `DATA_FILE=/cesta/todos.json` | `-DataFile` | `<repo>/data/todos.json` |
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

Při ručním spuštění nastavte každé instanci vlastní ID a log soubor (jinak píšou obě do téhož) a všem
**stejný sdílený soubor s daty** (jinak by každá pracovala se svými daty):

```bash
export JsonPersistence__Enabled=true JsonPersistence__Shared=true
export JsonPersistence__FilePath=/absolutni/cesta/data/todos.json

Instance__Id=api-1 FileLogging__IncludeInstanceId=true \
  dotnet run --project src/UAI.ZeroVP.API --no-launch-profile --urls http://localhost:5258
Instance__Id=api-2 FileLogging__IncludeInstanceId=true \
  dotnet run --project src/UAI.ZeroVP.API --no-launch-profile --urls http://localhost:5259
```

Port `0` (`--urls http://127.0.0.1:0`) nechá výběr volného portu na systému; skutečný port pak ukazuje
`/api/info` a řádek `Now listening on` v konzoli.

### Jak instance rozeznat

1. **`GET /api/info`** – každá instance vrací vlastní `instanceId`, `urls` (adresy, na kterých opravdu
   naslouchá), `port` a `storage` (použité úložiště). Původní pole (`machineName`, `environment`, `version`,
   `utcNow`, `uptime`, `todoCount`) zůstávají beze změny.

   ```bash
   curl http://localhost:5258/api/info   # "instanceId": "api-1", "port": 5258, "storage": {...}
   curl http://localhost:5259/api/info   # "instanceId": "api-2", "port": 5259, stejné "storage.location"
   ```

2. **Log soubory** – `src/UAI.ZeroVP.API/logs/zerovp-api-1-yyyyMMdd.log`, `zerovp-api-2-yyyyMMdd.log`, …
3. **Konzole** – událost `ApplicationStarted` uvádí adresu, na které instance naslouchá.

### Vyzkoušení sdílení dat

```bash
# vytvoření úkolu přes instanci 1 …
curl -X POST http://localhost:5258/api/todos -H "Content-Type: application/json" -d '{"title":"Z první instance"}'
# … a jeho přečtení přes instanci 2
curl http://localhost:5259/api/todos
```

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

Celé cílové nasazení běží v kontejnerech: **několik replik API nad jedním úložištěm**.
Soubory `Dockerfile`, `.dockerignore`, `docker-compose.yml` a `nginx/default.conf` jsou
v repozitáři. GitHub Actions kontejnery nesestavují ani netestují, CI běží bez Dockeru.

```text
klient ──> gateway (nginx, port 5258) ──> api × N (repliky, port 8080 jen uvnitř sítě)
                                              │
                                              └──> storage (svazek todo-data, soubor /data/todos.json)
```

| Služba | Role |
|---|---|
| `storage` | jediné úložiště: pojmenovaný svazek `todo-data` s JSON souborem sdíleným všemi replikami; připraví práva svazku a hlásí se jako `healthy` |
| `api` | API, škáluje se přepínačem `--scale api=N`; čeká na zdravé `storage`; bez published portu |
| `gateway` | nginx, jediný vstup na hostiteli (`http://localhost:5258`), rozděluje požadavky mezi všechny repliky; čeká na zdravé `api` |

Úložištěm je JSON soubor ve [sdíleném režimu](#sdílené-úložiště-více-instancí-jedna-databáze)
(`JsonPersistence__Shared=true`, `JsonPersistence__FilePath=/data/todos.json`), takže úkol vytvořený
jednou replikou vidí hned všechny ostatní. Data leží na svazku, a proto přežijí restart i znovuvytvoření
kontejnerů API.

### Spuštění a škálování

```bash
# sestavení image a spuštění tří replik API, jednoho úložiště a brány
docker compose up --build -d --scale api=3

# stav (storage, api × 3 a gateway by měly být "healthy")
docker compose ps

# změna počtu replik za běhu (brána si nové repliky najde sama)
docker compose up -d --scale api=5 --no-recreate
docker compose up -d --scale api=2 --no-recreate
```

Port brány na hostiteli lze změnit proměnnou `GATEWAY_PORT` (výchozí `5258`),
např. `GATEWAY_PORT=8081 docker compose up -d --scale api=3`.

### Ověření: repliky jsou rozlišitelné

Každá replika vrací v `GET /api/info` vlastní `instanceId` (jméno kontejneru + port, např. `3f9c1a2b7d10-8080`).
Opakovaným voláním přes bránu uvidíš různé repliky; hlavička `X-Upstream-Addr` ukazuje adresu repliky,
která požadavek obsloužila.

```bash
for i in 1 2 3 4 5 6; do curl -s http://localhost:5258/api/info | grep -o '"instanceId":"[^"]*"'; done
curl -si http://localhost:5258/api/info | grep -i x-upstream-addr
```

Všechny repliky ukazují stejné `storage` (`"type":"SharedJsonFile"`, `"shared":true`, `"location":"/data/todos.json"`).

### Ověření: data jsou sdílená

```bash
# úkol vytvořený přes bránu (libovolnou replikou) ...
curl -X POST http://localhost:5258/api/todos -H "Content-Type: application/json" \
  -d '{"title":"Úkol z první repliky"}'

# ... vidí každé volání, ať ho obslouží kterákoli replika
for i in 1 2 3 4 5 6; do curl -s http://localhost:5258/api/todos | grep -c "Úkol z první repliky"; done
```

Každý řádek výstupu musí být `1`.

### Ověření: data přežijí restart

```bash
docker compose restart api                              # restart replik
docker compose up -d --force-recreate --scale api=3 api # znovuvytvoření replik
curl -s http://localhost:5258/api/todos                 # úkoly jsou pořád tam
```

### Logy

Každá replika běží ve vlastním kontejneru a zapisuje do vlastního souboru
`/app/logs/zerovp-<instanceId>-yyyyMMdd.log` (`FileLogging__IncludeInstanceId=true`), takže se logy
replik nemíchají. Konzolový výstup všech replik:

```bash
docker compose logs api            # všechny repliky
docker compose logs -f api         # sledování
docker compose logs gateway        # přístupový log brány
```

### Úklid

```bash
docker compose down        # zastaví a odstraní kontejnery, DATA ZŮSTANOU na svazku todo-data
docker compose down -v     # odstraní i svazek = smaže všechna data (při dalším startu se vloží ukázkové úkoly)
```

### Konfigurace

Nastavení se předává výhradně proměnnými prostředí v `docker-compose.yml`:

| Proměnná | Význam |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | prostředí ASP.NET Core |
| `Swagger__Enabled` | zapnutí Swagger UI (`http://localhost:5258/swagger`) |
| `Todo__SeedSampleData` | vložení ukázkových úkolů, když soubor s daty ještě neexistuje (při souběžném startu ho vytvoří jen první replika) |
| `JsonPersistence__Enabled`, `JsonPersistence__Shared`, `JsonPersistence__FilePath` | sdílené úložiště: jeden soubor na svazku, u všech replik stejná cesta |
| `FileLogging__Directory`, `FileLogging__IncludeInstanceId` | složka souborových logů a ID repliky v názvu souboru |
| `Instance__Id` | volitelně pevné ID instance; bez něj je to `{hostname kontejneru}-{port}` |

Aplikace v kontejneru běží pod uživatelem bez práv roota a naslouchá na portu `8080`. Kontrola zdraví volá `/health`
z malého pomocného nástroje v image (runtime image nemá `curl`).

### Omezení

Zámek souboru funguje spolehlivě na lokálním svazku jednoho hostitele (viz
[sdílené úložiště](#sdílené-úložiště-více-instancí-jedna-databáze)). Bind mount sdílené složky z Windows/macOS
ani síťový disk (NFS/SMB) na to nepoužívejte; proto je `todo-data` pojmenovaný svazek. Repliky běží na jednom
stroji; pro nasazení na více strojích je potřeba skutečná databáze.