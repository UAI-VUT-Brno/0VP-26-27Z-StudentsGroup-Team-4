# CI pipeline a bezpečnostní skenování (DevSecOps)

Repozitář používá GitHub Actions (`.github/workflows/ci.yml`). Pipeline se spouští při `push` do větví `main`, `feature/**`, `fix/**`, `perf/**` a při pull requestech do `main`. Skládá se ze dvou paralelních jobů:

| Job | Co dělá |
|---|---|
| `build-and-test` | Restore, build v konfiguraci Release a unit testy (bez kategorie `Performance`). |
| `security-scan` | Bezpečnostní brána: spouští `scripts/security-scan.ps1`. Při jakémkoli nálezu job selže. |

## Bezpečnostní kontroly

Skript `scripts/security-scan.ps1` provádí tři kontroly. Lokálně i v CI se spouští stejný skript, takže se výsledky nemohou rozcházet.

### 1. Zranitelné NuGet balíčky
- Konfigurace: `Directory.Build.props` (`NuGetAudit`, `NuGetAuditMode=all`, varování `NU1901`–`NU1904` povýšena na chyby).
- Kontrolují se přímé i tranzitivní závislosti. Při nálezu selže už `dotnet restore` i `dotnet build`.
- Skript navíc vypíše přehlednou tabulku přes `dotnet list package --vulnerable`.

### 2. Statická analýza kódu (Roslyn analyzátory)
- Konfigurace: `Directory.Build.props` (`EnableNETAnalyzers`) a `.editorconfig` (`dotnet_analyzer_diagnostic.category-Security.severity = error`).
- Všechna pravidla kategorie Security (např. `CA5351` – slabý kryptografický algoritmus) jsou chyby, takže build při nálezu selže. Ostatní pravidla zůstávají na výchozí úrovni.

### 3. Detekce úniku secretů (Gitleaks)
- Skenuje zdrojové soubory i historii Gitu (v CI se klonuje celá historie, `fetch-depth: 0`).
- Konfigurace: `.gitleaks.toml` rozšiřuje výchozí pravidla gitleaks (`useDefault = true`) a ignoruje složku `tests/`, kde jsou záměrně smyšlená data. Tajemství v testech se tedy nezachytí.
- V CI se gitleaks stahuje v pevné verzi a ověřuje se jeho kontrolní součet. Skript se tam spouští s `-RequireGitleaks`, takže chybějící gitleaks je chyba.

## Závislosti: doporučení místo automatických PR

Dependabot **nevytváří žádné větve ani pull requesty**. Aktualizace závislostí děláme vědomě a ručně, Dependabot nás pouze upozorňuje, co stojí za zvážení.

### Konfigurace Dependabota
- `.github/dependabot.yml` má pro NuGet i GitHub Actions nastaveno `open-pull-requests-limit: 0`, takže pro verzní aktualizace žádné PR nevzniká.
- V *Settings → Advanced Security* jsou **Dependabot security updates** (včetně grouped) vypnuté, takže PR nevznikají ani kvůli zranitelnostem.
- **Dependabot alerts** zůstávají zapnuté. Zranitelné balíčky se zobrazují v záložce *Security → Dependabot* a nic do repozitáře nepushují. Slouží jako doporučení, které balíčky je vhodné aktualizovat.

### Zjištění zastaralých balíčků
Přehled balíčků, které mají novější verzi, získáš lokálně:
```powershell
dotnet list package --outdated
dotnet list package --vulnerable --include-transitive
```

### Jak aktualizovat balíček ručně
1. Vyber balíček z alertu v záložce Security nebo z výpisu příkazů výše a zvaž, zda aktualizace dává smysl (major verze může obsahovat breaking changes).
2. Vytvoř krátkodobou větev (např. `fix/update-<balicek>`).
3. Aktualizuj balíček, např. `dotnet add <projekt> package <Balicek> --version <verze>`.
4. Spusť `dotnet build`, `dotnet test` a `scripts/security-scan.ps1`.
5. Otevři pull request do `main`. Po zelené CI ho sluč a větev smaž.

## Lokální spuštění

1. Nainstaluj gitleaks: `winget install gitleaks` (po instalaci otevři nový terminál).
2. V kořeni repozitáře spusť:
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts/security-scan.ps1
   ```
3. Výsledek: `SECURITY SCAN: OK` (kód 0) nebo `SECURITY SCAN: SELHAL` (kód 1). Pokud gitleaks není nainstalovaný, skript kontrolu secretů přeskočí a výslovně na to upozorní. Přepínač `-RequireGitleaks` z toho udělá chybu.

## Co dělat při nálezu

- **Zranitelný balíček:** aktualizuj balíček na opravenou verzi (odkaz na advisory je ve výpisu) podle postupu v části „Jak aktualizovat balíček ručně". Automatický PR od Dependabota nevznikne.
- **Chyba analyzátoru `CAxxxx`:** oprav kód podle odkazu v chybové hlášce.
- **Uniklý secret:** považuj ho za kompromitovaný, zneplatni ho (rotace klíče/tokenu) a teprve potom ho odstraň z kódu. Samotné smazání souboru ho z historie Gitu neodstraní.

## Povinná brána pro merge

V GitHubu (Settings → Branches → pravidlo pro `main`) je job `security-scan` nastaven jako povinná kontrola, takže pull request s nálezem nelze sloučit.