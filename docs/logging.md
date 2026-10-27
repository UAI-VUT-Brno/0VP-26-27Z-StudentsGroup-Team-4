# Logování aplikace

API zapisuje do trvalého logu svou aktivitu: **start a zastavení aplikace**, **každý HTTP požadavek**
a **chyby**. U každé události se zaznamenává čas, identifikace události, výsledek a – nastala-li
chyba – popis chyby.

## Stručně: metoda a úložiště

| | |
|---|---|
| **Metoda** | Standardní `Microsoft.Extensions.Logging` (`ILogger`) + vlastní poskytovatel `JsonFileLoggerProvider`. Žádný další NuGet balíček. |
| **Formát** | JSON Lines – jeden záznam = jeden řádek JSON, kódování UTF-8 bez BOM, konec řádku `\n`. |
| **Úložiště** | Soubory ve složce `logs/` (relativně ke kořeni aplikace), jeden soubor na den: `zerovp-yyyyMMdd.log` (datum v UTC). Při `FileLogging__IncludeInstanceId=true` je v názvu i ID instance: `zerovp-{instanceId}-yyyyMMdd.log`. |
| **Retence** | Ponechá se 14 nejnovějších souborů, starší se při přepnutí na nový den smažou. |
| **Konzole** | Zůstává zapnutá beze změny (`docker logs` funguje dál). |

Zápis do souboru probíhá na pozadí přes frontu, takže pomalý disk nebrzdí odpovědi API.
Chyba při zápisu (plný disk, chybějící práva) aplikaci neshodí – vypíše se na `stderr`.

Kde je kód: `src/UAI.ZeroVP.API/Logging/`. Do `Program.cs` se zapojuje třemi řádky
(`AddApplicationLogging`, `UseRequestLogging` a řízený start/stop místo `app.Run()`).

### Kde log najdu

| Způsob spuštění | Složka s logy |
|---|---|
| `dotnet run --project src/UAI.ZeroVP.API` | `src/UAI.ZeroVP.API/logs/` |
| Docker kontejner | `/app/logs/` (uvnitř kontejneru) |

> **Docker a persistence.** Soubor v kontejneru zmizí spolu s kontejnerem – stejně jako data
> v paměti (viz README, sekce Úložiště dat). Aby log restart přežil, připojte svazek a nasměrujte
> do něj log:
>
> ```bash
> docker run -v zerovp-logs:/var/log/zerovp -e FileLogging__Directory=/var/log/zerovp ...
> ```
>
> Uživatel, pod kterým aplikace v kontejneru běží, musí mít do složky právo zápisu.

## Nastavení

V `appsettings.json` (nebo proměnnou prostředí, dvojité podtržítko = oddělovač sekcí):

```jsonc
"FileLogging": {
  "Enabled": true,          // FileLogging__Enabled=false vypne zápis do souboru
  "Directory": "logs",      // relativní (ke kořeni aplikace) nebo absolutní cesta
  "FileNamePrefix": "zerovp",
  "IncludeInstanceId": false, // true = do názvu souboru přidá ID instance (Instance:Id), viz níže
  "RetainedFileCount": 14   // 0 = nemazat staré soubory
},
"RequestLogging": {
  "ExcludedPaths": [ "/health" ]   // tyto cesty se nelogují (HEALTHCHECK by log zahltil)
},
"Logging": {
  "JsonFile": {
    "LogLevel": { "Default": "Information", "Microsoft": "Warning", "System": "Warning" }
  }
}
```

Minimální úroveň zapisovaná do souboru se řídí sekcí `Logging:JsonFile:LogLevel`
(nezávisle na konzoli). Hlášení frameworku (`Microsoft.*`) se do souboru dostanou až od
úrovně `Warning`, aby se neduplikovaly vlastní události životního cyklu.

### Více instancí na jednom stroji

Instance spuštěné vedle sebe se stejnou složkou `logs/` by psaly do téhož souboru a jejich záznamy by se
prolínaly. Proto existuje volba `FileLogging:IncludeInstanceId` (proměnná `FileLogging__IncludeInstanceId`):

| Hodnota | Název souboru |
|---|---|
| `false` (výchozí) | `zerovp-20261007.log` – beze změny, vhodné pro jednu instanci |
| `true` | `zerovp-api-1-20261007.log` – `api-1` je ID instance (`Instance__Id`; když není zadané, `{MachineName}-{port}`) |

Znaky mimo `A–Z a–z 0–9 . _ -` se v ID instance v názvu souboru nahradí `_`, ID se zkrátí na 80 znaků.
Retence maže jen soubory se stejným prefixem **vlastní instance** (`zerovp-api-*` nesmaže `zerovp-api-2-*`).
Skripty `scripts/run-instances.ps1` a `.sh` volbu zapínají samy; postup je v README v sekci
„Více instancí na localhostu“.

## Struktura záznamu

Každý řádek obsahuje tato pole (pořadí je pevné):

| Pole | Typ | Popis |
|---|---|---|
| `timestamp` | string | Čas události v UTC, ISO 8601, milisekundy: `2026-10-07T10:15:40.502Z`. |
| `level` | string | `Information`, `Warning`, `Error`, `Critical` (případně `Debug`/`Trace`, pokud je zapnete). |
| `eventId` | number | Číselný identifikátor události (viz katalog níže). `0` u záznamů bez identifikátoru (hlášení frameworku). |
| `event` | string \| null | Název události, např. `RequestCompleted`. `null`, pokud událost identifikátor nemá. |
| `result` | string | Výsledek: `Success`, `Failure` nebo `Cancelled` (klient zrušil požadavek). |
| `message` | string | Čitelná zpráva. |
| `error` | string \| null | Popis chyby; `null`, pokud k chybě nedošlo. U výjimky `TypVýjimky: zpráva`, u HTTP chyby např. `HTTP 404 Not Found`. |
| `category` | string | Třída, která záznam zapsala. |
| `properties` | object | Doplňující údaje události (liší se podle události). Pole chybí, pokud nejsou žádné. |
| `exception` | string | Úplná výjimka včetně zásobníku volání. Pole je jen u záznamů s výjimkou. |

**Jak se určuje `result`:** událost ho uvede sama (požadavky: `Success` pro stavové kódy pod 400,
jinak `Failure`). Pokud ne, odvodí se – při výjimce nebo úrovni `Error` a vyšší je `Failure`,
jinak `Success`.

## Katalog událostí

| `eventId` | `event` | Úroveň | Kdy se zapíše | `properties` |
|---|---|---|---|---|
| 1000 | `ApplicationStarting` | Information | Aplikace byla sestavena a začíná se spouštět. | `Environment`, `Version` |
| 1001 | `ApplicationStarted` | Information | Server naslouchá a přijímá požadavky. | `Urls` |
| 1002 | `ApplicationStopping` | Information | Přišel pokyn k ukončení (Ctrl+C, SIGTERM, `docker stop`). | – |
| 1003 | `ApplicationStopped` | Information | Aplikace se korektně zastavila. | – |
| 1004 | `ApplicationFailed` | Critical | Start nebo běh skončil neošetřenou chybou (např. obsazený port). Proces skončí s kódem 1. | – (výjimka v `exception`) |
| 2000 | `RequestCompleted` | Information / Warning / Error | Požadavek dokončen: stavový kód < 400 = Information, 4xx = Warning, 5xx = Error. | `Method`, `Path`, `Route`, `StatusCode`, `DurationMs`, `TraceId` |
| 2001 | `RequestFailed` | Error | Požadavek skončil neošetřenou výjimkou (klient dostane 500). | stejné jako 2000 |
| 2002 | `RequestAborted` | Warning | Klient se odpojil dřív, než byl požadavek zpracován (`result` = `Cancelled`). | stejné jako 2000 |
| 3000 | `TodoCreated` | Information | Byl vytvořen úkol. | `TodoId`, `Title` |
| 3001 | `TodoDeleted` | Information | Úkol byl smazán. | `TodoId` |
| 3002 | `WeeklyTodosCreated` | Information | A weekly sequence of tasks was created (`POST /api/todos/repeat`). | `Count`, `Title`, `FirstDueAtUtc`, `LastDueAtUtc` |

`TraceId` je identifikátor požadavku z ASP.NET Core (odpovídá `HttpContext.TraceIdentifier`),
`Route` je šablona trasy (`api/todos/{id:guid}`), takže jde agregovat podle endpointu
bez ohledu na konkrétní `id`.

### Co se záměrně nelogují

Query string, hlavičky, tělo požadavku a IP adresa klienta (mohou obsahovat osobní údaje).
Pozor: u událostí 3000 se zapisuje `Title` úkolu, tedy text zadaný uživatelem.

Hodnoty z požadavků (cesta, název úkolu) jsou v JSON escapované, takže nelze do logu
„podvrhnout“ další řádek pomocí znaku nového řádku.

## Příklady

Záznamy jsou v souboru každý na jednom řádku; zde jsou pro čitelnost zalomené.

**Start aplikace**

```json
{"timestamp":"2026-10-07T10:15:02.114Z","level":"Information","eventId":1000,"event":"ApplicationStarting","result":"Success","message":"Aplikace se spouští (prostředí Development, verze 1.0.0).","error":null,"category":"UAI.ZeroVP.API.Logging.ApplicationLifecycleLogger","properties":{"Environment":"Development","Version":"1.0.0"}}
{"timestamp":"2026-10-07T10:15:02.531Z","level":"Information","eventId":1001,"event":"ApplicationStarted","result":"Success","message":"Aplikace byla spuštěna a naslouchá na http://localhost:5258.","error":null,"category":"UAI.ZeroVP.API.Logging.ApplicationLifecycleLogger","properties":{"Urls":"http://localhost:5258"}}
```

**Úspěšný požadavek** – `GET /api/todos`

```json
{"timestamp":"2026-10-07T10:15:40.502Z","level":"Information","eventId":2000,"event":"RequestCompleted","result":"Success","message":"HTTP GET /api/todos -> 200 za 4.3 ms","error":null,"category":"UAI.ZeroVP.API.Logging.RequestLoggingMiddleware","properties":{"Method":"GET","Path":"/api/todos","Route":"api/todos","StatusCode":200,"DurationMs":4.3,"TraceId":"0HNG3K2P1QJ7M:00000001"}}
```

**Vytvoření úkolu** – dva záznamy: obchodní událost a požadavek

```json
{"timestamp":"2026-10-07T10:16:11.870Z","level":"Information","eventId":3000,"event":"TodoCreated","result":"Success","message":"Vytvořen úkol 5b1c9f0e-2d7a-4c1e-9a52-7f3e8d6b4a10 (Napsat Dockerfile).","error":null,"category":"UAI.ZeroVP.API.Controllers.TodosController","properties":{"TodoId":"5b1c9f0e-2d7a-4c1e-9a52-7f3e8d6b4a10","Title":"Napsat Dockerfile"}}
{"timestamp":"2026-10-07T10:16:11.874Z","level":"Information","eventId":2000,"event":"RequestCompleted","result":"Success","message":"HTTP POST /api/todos -> 201 za 6.8 ms","error":null,"category":"UAI.ZeroVP.API.Logging.RequestLoggingMiddleware","properties":{"Method":"POST","Path":"/api/todos","Route":"api/todos","StatusCode":201,"DurationMs":6.8,"TraceId":"0HNG3K2P1QJ7M:00000002"}}
```

**Chyba klienta** – úkol neexistuje (`result` = `Failure`, `error` vyplněno)

```json
{"timestamp":"2026-10-07T10:17:05.219Z","level":"Warning","eventId":2000,"event":"RequestCompleted","result":"Failure","message":"HTTP GET /api/todos/00000000-0000-0000-0000-000000000001 -> 404 za 1.8 ms","error":"HTTP 404 Not Found","category":"UAI.ZeroVP.API.Logging.RequestLoggingMiddleware","properties":{"Method":"GET","Path":"/api/todos/00000000-0000-0000-0000-000000000001","Route":"api/todos/{id:guid}","StatusCode":404,"DurationMs":1.8,"TraceId":"0HNG3K2P1QJ7M:00000003"}}
```

**Chyba serveru** – neošetřená výjimka (pole `exception` zkráceno)

```json
{"timestamp":"2026-10-07T10:18:30.044Z","level":"Error","eventId":2001,"event":"RequestFailed","result":"Failure","message":"HTTP GET /api/info -> 500 za 12.4 ms","error":"InvalidOperationException: Něco se pokazilo.","category":"UAI.ZeroVP.API.Logging.RequestLoggingMiddleware","properties":{"Method":"GET","Path":"/api/info","Route":"api/info","StatusCode":500,"DurationMs":12.4,"TraceId":"0HNG3K2P1QJ7M:00000004"},"exception":"System.InvalidOperationException: Něco se pokazilo.\n   at UAI.ZeroVP.API.Controllers.InfoController.Get(CancellationToken cancellationToken) in ...\n   ..."}
```

**Zastavení aplikace** (Ctrl+C / `docker stop`)

```json
{"timestamp":"2026-10-07T11:02:57.310Z","level":"Information","eventId":1002,"event":"ApplicationStopping","result":"Success","message":"Aplikace se zastavuje.","error":null,"category":"UAI.ZeroVP.API.Logging.ApplicationLifecycleLogger"}
{"timestamp":"2026-10-07T11:02:57.352Z","level":"Information","eventId":1003,"event":"ApplicationStopped","result":"Success","message":"Aplikace byla zastavena.","error":null,"category":"UAI.ZeroVP.API.Logging.ApplicationLifecycleLogger"}
```

**Start selhal** – port je obsazený

```json
{"timestamp":"2026-10-07T11:05:12.906Z","level":"Critical","eventId":1004,"event":"ApplicationFailed","result":"Failure","message":"Aplikace skončila neočekávanou chybou.","error":"IOException: Failed to bind to address http://localhost:5258: address already in use.","category":"UAI.ZeroVP.API.Logging.ApplicationLifecycleLogger","exception":"System.IO.IOException: Failed to bind to address http://localhost:5258: address already in use.\n   ..."}
```

## Práce s logem

```bash
# sledování za běhu
tail -f src/UAI.ZeroVP.API/logs/zerovp-*.log

# jen chyby (jq)
jq -c 'select(.result == "Failure")' logs/zerovp-20261007.log

# nejpomalejší požadavky
jq -s -c '[.[] | select(.eventId == 2000)] | sort_by(-.properties.DurationMs) | .[:10][] | {path: .properties.Path, ms: .properties.DurationMs}' logs/*.log

# historie startů a zastavení
jq -c 'select(.eventId >= 1000 and .eventId < 2000) | {timestamp, event, result}' logs/*.log
```

## Testy

Chování loggeru ověřují automatické testy v `tests/UAI.ZeroVP.Tests/Logging/`:

```bash
dotnet test tests/UAI.ZeroVP.Tests
```

| Testovací třída | Co dokazuje |
|---|---|
| `JsonFileLoggerProviderTests` | povinná pole záznamu, UTC čas, čitelná diakritika, odvození `result`, výjimka v `error`/`exception`, nemožnost podvrhnout řádek logu, zachování pořadí, souběžný zápis z více vláken, nový soubor po půlnoci, retence, odolnost vůči nezapisovatelné složce |
| `RequestLoggingMiddlewareTests` | úspěch / 4xx / 5xx, výjimka (zalogována a znovu vyhozena), odpojení klienta, `Route`, vynechané cesty, query string se nelogují |
| `LogFileNamingTests`, `JsonFileLoggerProviderInstanceTests` | název souboru s ID instance a bez něj, ošetření nevhodných znaků, dvě instance = dva soubory, retence nesmaže soubory jiné instance |
| `ApplicationLifecycleLoggingTests` | skutečný server: `ApplicationStarting` → `Started` → požadavky → `Stopping` → `Stopped` se zapíšou do souboru; selhání startu = `ApplicationFailed` |

## Omezení

- Při násilném ukončení procesu (`kill -9`, `docker kill`, pád procesu, výpadek stroje) se
  `ApplicationStopping`/`ApplicationStopped` nezapíšou – aplikace o ukončení nemá šanci vědět.
  Po neukončeném `ApplicationStarting` bez navazujícího `ApplicationStopped` poznáte, že běh skončil nečistě.
- Více replik zapisujících do **téhož** souboru (sdílený svazek) se v zápisu prolíná; každá replika
  by měla mít vlastní složku, vlastní název souboru (`FileLogging__IncludeInstanceId=true`
  + `Instance__Id`), nebo se má logovat na `stdout` a sbírat Dockerem.
- Při přeplnění fronty (výchozí 10 000 záznamů, tj. disk stojí) se nejnovější záznamy zahazují.