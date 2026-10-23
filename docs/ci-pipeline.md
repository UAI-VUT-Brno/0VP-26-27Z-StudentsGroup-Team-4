# CI/CD Pipeline a Bezpečnostní Skenování (DevSecOps)

Tento dokument popisuje automatizovanou integraci (CI) a bezpečnostní brány, které chrání tento repozitář. Všechny kontroly běží automaticky přes GitHub Actions, ale dají se pro rychlou kontrolu spouštět i lokálně.

## GitHub Actions Workflow (`ci.yml`)

Pipeline se spouští při každém `push` a `pull_request` a skládá se ze dvou paralelních jobů:

1. **build-and-test**: Zkompiluje projekt v režimu Release a spustí všechny unit testy.
2. **security-scan**: Provádí bezpečnostní audity kódu a závislostí. Pokud jakákoliv kontrola selže, celý build spadne (automatická brána).

---

## Bezpečnostní kontroly (Security Skenování)

Abychom splnili DevSecOps standardy, zavedli jsme do projektu několik vrstev ochrany.

### 1. Zranitelné NuGet balíčky
Kontrolujeme, zda projekt neobsahuje balíčky se známou zranitelností (včetně tranzitivních závislostí).
* **Konfigurace:** `Directory.Build.props` (zapnutý `NuGetAudit` a povýšení varování `NU190x` na chyby).
* **Lokální spuštění:** Spustí se automaticky při každém `dotnet restore` nebo `dotnet build`. Build v případě nálezu selže.

### 2. Statická analýza kódu (SAST)
Při buildu běží vestavěné Roslyn analyzátory, které hlídají kvalitu kódu a bezpečnostní rizika.
* **Konfigurace:** Zapnuto v `Directory.Build.props` (`EnableNETAnalyzers`).
* **Lokální spuštění:** `dotnet build` vypíše případná varování do konzole (neshazuje build).

### 3. Detekce úniku secretů (Gitleaks)
Skener prochází kód a historii Gitu, aby zabránil commitnutí hesel, API klíčů nebo tokenů.
* **Konfigurace v CI:** Stahuje a spouští open-source binárku přímo v běžícím runneru.
* **Výjimky (`.gitleaks.toml`):** Složka `tests` je záměrně ignorována pomocí povolovacích pravidel. V unit testech se často vyskytují "hardcoded" smyšlená hesla, která nejdou do produkce a způsobovala by falešné poplachy.

### 4. Automatické aktualizace (Dependabot)
GitHub Dependabot pravidelně kontroluje nové verze NuGet balíčků a samotných GitHub Actions. Pokud najde novou verzi, automaticky vytvoří Pull Request s aktualizací.
* **Konfigurace:** `.github/dependabot.yml` (nastaveno na týdenní kontrolu).

---

## Jak otestovat bezpečnost lokálně

Pro rychlou kontrolu před samotným commitem slouží připravený PowerShell skript.

1. Otevři terminál v kořeni repozitáře.
2. Spusť skript:
   ```powershell
   .\scripts\security-scan.ps1