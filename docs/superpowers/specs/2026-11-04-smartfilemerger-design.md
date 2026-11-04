# Design-Spezifikation: SmartFileMerger

**Datum:** 2026-11-04  
**Status:** Approved by User  
**Autor:** Marc Rambow & Antigravity  
**Repository:** [github.com/mrambow/smart-file-merger](https://github.com/mrambow/smart-file-merger)  
**Target:** .NET 10.0 (C# 14), Windows Console Application  

---

## 1. Übersicht & Zielsetzung

`SmartFileMerger` ist ein robustes, interaktives CLI-Werkzeug für Windows zum sicheren Zusammenführen (Mergen) zweier Verzeichnisbäume (`Source / A` nach `Target / B`). 

### Kernmerkmale:
1. **Strukturerhalt:** Überträgt Dateien unter Beibehaltung der relativen Pfad- und Ordnerhierarchie.
2. **Non-Blocking Conflict Collection:** Konfliktfreie Dateien werden sofort verschoben; existierende Zieldateien stoppen den Prozess nicht, sondern werden in einer Konflikt-Warteschlange gesammelt.
3. **Automatische Duplikat-Behandlung:** 100% identische Dateien (Größe und sekundengenauer Zeitstempel) werden automatisch erkannt und ohne lästige Nutzerabfragen bereinigt/aufgelöst.
4. **Interaktiver Review:** Am Ende werden verbleibende Konflikte mit einer farbigen Metadaten-Differenztabelle (Größe, Datum) präsentiert und über 4 modulare Strategien gelöst (Überschreiben, Ziel sichern & nachziehen, Quelle umbenennen, Überspringen).
5. **Windows-Dateisystem-Robustheit:** Handhabt ReadOnly-Flags bei Overwrites, bewahrt Zeitstempel auch über Laufwerksgrenzen hinweg (Cross-Volume / NAS) und fängt Dateisperren (`IOException`) fehlertolerant ab.
6. **Interaktiver Wizard mit Simulation:** Vollständige Schritt-für-Schritt-Führung via `Spectre.Console` inklusive optionalem Simulationsmodus (`--dry-run`).
7. **Public Open Source & Maker-Checker:** Saubere Trennung von `src/` und `tests/`, `AGENTS.md`, `README.md`, MIT-Lizenz und CI-Absicherung über den `claude-pr-gate` Review-Gatekeeper mit Maintainer-Label-Schutz.
8. **Update-Hinweis:** Nicht-blockierende Prüfung gegen die GitHub Releases API beim Start mit dezentem Hinweis bei neueren Versionen.

---

## 2. Architektur & Solution-Design

### 2.1 Verzeichnis- & Dateistruktur

```text
smart-file-merger/
├── .github/
│   └── workflows/
│       ├── ci.yml                 # Build, Lint & Unit Tests bei PR/Push
│       └── claude-pr-gate.yml     # Maker-Checker via mrambow/claude-pr-gate (Maintainer-Gated)
├── src/
│   └── SmartFileMerger/
│       ├── SmartFileMerger.csproj # net10.0, Nullable: enable, ImplicitUsings: enable
│       ├── Program.cs             # Entry Point & DI Composition Root
│       ├── Domain/
│       │   ├── Models/
│       │   │   ├── DirectoryRoot.cs
│       │   │   ├── RelativeFilePath.cs
│       │   │   ├── FileMetadata.cs
│       │   │   ├── FileConflict.cs
│       │   │   └── MergeSummary.cs
│       │   └── Interfaces/
│       │       ├── IFileSystemService.cs
│       │       ├── IConflictResolutionStrategy.cs
│       │       ├── IUserInterface.cs
│       │       └── IUpdateCheckService.cs
│       ├── Application/
│       │   ├── MergeOrchestrator.cs
│       │   ├── ConflictResolutionRunner.cs
│       │   └── Strategies/
│       │       ├── OverwriteTargetStrategy.cs
│       │       ├── MoveOrRenameTargetStrategy.cs
│       │       ├── RenameSourceStrategy.cs
│       │       └── SkipConflictStrategy.cs
│       ├── Infrastructure/
│       │   ├── WindowsFileSystemService.cs
│       │   └── GitHubUpdateCheckService.cs
│       └── Presentation/
│           └── SpectreConsoleUserInterface.cs
├── tests/
│   └── SmartFileMerger.Tests/
│       ├── SmartFileMerger.Tests.csproj # xUnit, NSubstitute, FluentAssertions
│       ├── Domain/
│       │   ├── DirectoryRootTests.cs
│       │   ├── RelativeFilePathTests.cs
│       │   └── FileConflictTests.cs
│       ├── Application/
│       │   ├── MergeOrchestratorTests.cs
│       │   └── StrategyTests.cs
│       └── Infrastructure/
│           └── WindowsFileSystemServiceIntegrationTests.cs
├── docs/
│   └── superpowers/specs/
│       └── 2026-11-04-smartfilemerger-design.md
├── .editorconfig
├── .gitignore
├── AGENTS.md
├── LICENSE (MIT)
└── README.md
```

### 2.2 Architektur-Leitlinien
- **Pragmatisches DDD:** Saubere Entkopplung von Fachlogik (Domain) von physischem Dateisystem (Infrastructure) und Terminal-UI (Presentation).
- **Starke Typisierung:** Primitive Typen werden für Pfade vermieden; stattdessen `DirectoryRoot` und `RelativeFilePath`.
- **Ponytail-Prinzip:**
  - Öffentliche Methoden lesen sich wie ein Inhaltsverzeichnis (High-Level Summary).
  - Maximale Einrückungstiefe: 2.
  - Guard Clauses / Early Returns ganz oben in jeder Methode.
  - Keine Magic Strings oder Magic Numbers.
- **Open/Closed Principle:** Neue Konfliktstrategien können durch Implementierung von `IConflictResolutionStrategy` ergänzt werden, ohne existierende Klassen anzupassen.

---

## 3. Domain Layer

### 3.1 Value Objects & Records

```csharp
namespace SmartFileMerger.Domain.Models;

/// <summary>
/// Kapselt einen normalisierten, absoluten Verzeichnispfad und prüft Gültigkeit.
/// </summary>
public sealed record DirectoryRoot
{
    public string FullPath { get; }

    public DirectoryRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Pfad darf nicht leer sein.", nameof(path));

        // Entfernt umschließende Quotes (z.B. von Drag & Drop) und normalisiert
        var cleaned = path.Trim().Trim('"', '\'');
        FullPath = Path.GetFullPath(cleaned).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public string CombineWith(RelativeFilePath relativePath) =>
        Path.Combine(FullPath, relativePath.Value);

    public bool IsSubdirectoryOf(DirectoryRoot potentialParent)
    {
        var parentPath = potentialParent.FullPath + Path.DirectorySeparatorChar;
        var thisPath = FullPath + Path.DirectorySeparatorChar;
        return thisPath.StartsWith(parentPath, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Kapselt einen relativen Dateipfad und verhindert versehentliche Verwendung absoluter Pfade.
/// </summary>
public sealed record RelativeFilePath
{
    public string Value { get; }

    public RelativeFilePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("Relativer Pfad darf nicht leer sein.", nameof(relativePath));

        var cleaned = relativePath.Trim().Trim('"', '\'');
        if (Path.IsPathRooted(cleaned))
            throw new ArgumentException("Pfad muss relativ sein.", nameof(relativePath));

        // Vereinheitlicht Slashes auf den Plattform-Separator
        Value = cleaned.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }

    public string FileName => Path.GetFileName(Value);
    public string Extension => Path.GetExtension(Value);
    public string FileNameWithoutExtension => Path.GetFileNameWithoutExtension(Value);
    public string? DirectoryName => Path.GetDirectoryName(Value);
}

/// <summary>
/// Metadaten einer Datei für Vergleich und Visualisierung.
/// </summary>
public sealed record FileMetadata(
    string FullPath,
    long SizeInBytes,
    DateTime CreatedAtUtc,
    DateTime LastWriteAtUtc);

/// <summary>
/// Repräsentiert eine Namenskollision zwischen Quelle und Ziel.
/// </summary>
public sealed record FileConflict(
    RelativeFilePath RelativePath,
    FileMetadata SourceFile,
    FileMetadata TargetFile)
{
    public long SizeDifferenceInBytes => SourceFile.SizeInBytes - TargetFile.SizeInBytes;
    public TimeSpan AgeDifference => SourceFile.LastWriteAtUtc - TargetFile.LastWriteAtUtc;

    /// <summary>
    /// Erkennt 100% identische Dateien anhand gleicher Bytegröße und sekundengenauem Änderungsdatum.
    /// </summary>
    public bool IsIdenticalSizeAndTimestamp =>
        SizeDifferenceInBytes == 0 && Math.Abs(AgeDifference.TotalSeconds) < 2;
}

/// <summary>
/// Zusammenfassung des gesamten Merge-Laufs.
/// </summary>
public sealed record MergeSummary(
    int TotalFilesScanned,
    int FilesMovedDirectly,
    int IdenticalFilesSkipped,
    int ConflictsResolved,
    int ConflictsSkipped,
    int ErrorsOccurred,
    TimeSpan ElapsedDuration,
    bool IsDryRun);
```

### 3.2 Schnittstellen

```csharp
namespace SmartFileMerger.Domain.Interfaces;

public interface IFileSystemService
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
    void CreateDirectory(string path);
    IReadOnlyList<RelativeFilePath> GetAllRelativeFilePaths(DirectoryRoot root);
    FileMetadata GetMetadata(string fullPath);
    void EnsureDirectoryExistsForFile(string fullFilePath);
    void MoveFile(string sourceFullPath, string targetFullPath, bool overwrite = false);
    void DeleteFile(string fullPath);
    void RemoveEmptyDirectories(DirectoryRoot root);
}

public interface IConflictResolutionStrategy
{
    string Key { get; }
    string DisplayName { get; }
    void Execute(FileConflict conflict, DirectoryRoot sourceRoot, DirectoryRoot targetRoot);
}

public interface IUserInterface
{
    void ShowHeader();
    void ShowUpdateNotice(string latestVersion, string releaseUrl);
    (DirectoryRoot Source, DirectoryRoot Target, bool IsDryRun) PromptConfiguration();
    bool ConfirmMerge(DirectoryRoot source, DirectoryRoot target, bool isDryRun);
    void RunWithProgress(int totalFiles, Action<Action<string, int>> work);
    void DisplayMergeSummary(MergeSummary summary);
    IConflictResolutionStrategy PromptForConflictResolution(
        FileConflict conflict,
        int currentIndex,
        int totalConflicts,
        IReadOnlyList<IConflictResolutionStrategy> availableStrategies);
    string PromptForNewFileName(string defaultFileName, string promptMessage);
    string PromptForCustomTargetFilePath(string defaultFullPath);
    void ShowInfo(string message);
    void ShowWarning(string message);
    void ShowError(string message);
}

public interface IUpdateCheckService
{
    Task<(bool HasUpdate, string LatestVersion, string ReleaseUrl)> CheckForUpdateAsync(
        string currentVersion,
        CancellationToken cancellationToken = default);
}
```

---

## 4. Application Layer: Workflows

### 4.1 Phase 1: `MergeOrchestrator`
Verantwortlich für das Scannen von `Source`, das direkte Verschieben freier Dateien, das automatische Behandeln identischer Duplikate und das Sammeln verbleibender Konflikte in die `ConflictQueue`.

**Ablauf:**
1. **Guard Clauses:**
   - Quellordner existiert? Wenn nein -> Exception / Abbruch.
   - Quell- und Zielordner identisch? Wenn ja -> Exception / Abbruch.
   - Zielordner ein Unterordner der Quelle? Wenn ja -> Exception / Abbruch (Schutz vor Endlosschleifen).
   - Zielordner anlegen, falls noch nicht existent.
2. **Scan:** Alle relativen Dateipfade in `Source` ermitteln.
3. **Partitionierung pro Datei:**
   - Zielpfad ermitteln (`targetRoot.CombineWith(relPath)`).
   - **Fall A (Kein Konflikt):** Wenn Zieldatei noch nicht existiert:
     - Zielverzeichnis sicherstellen.
     - `_fileSystem.MoveFile(source, target)` (bzw. Simulation bei `IsDryRun`).
     - `FilesMovedDirectly++`.
   - **Fall B (Konflikt):** Wenn Zieldatei bereits existiert:
     - Metadaten von Quelle und Ziel laden.
     - Konfliktobjekt erzeugen.
     - **Unterfall B.1 (Identisch):** Wenn `conflict.IsIdenticalSizeAndTimestamp`:
       - Quelldatei löschen (`_fileSystem.DeleteFile(source)`), da Datei am Ziel bereits existiert.
       - `IdenticalFilesSkipped++`.
     - **Unterfall B.2 (Echter Konflikt):** 
       - In `ConflictQueue` einreihen.
4. **Fehlertoleranz:** Tritt bei einer einzelnen Datei eine `IOException` (z. B. File Lock) auf, wird der Fehler geloggt, `ErrorsOccurred++` erhöht und die nächste Datei verarbeitet.

### 4.2 Phase 2: `ConflictResolutionRunner`
Iteriert über alle Einträge in der `ConflictQueue`:
1. UI rendert die Differenztabelle des Konflikts.
2. Der Nutzer wählt eine der 4 Strategien:
   - **`OverwriteTargetStrategy`:** Zieldatei wird überschrieben (`MoveFile(src, dst, overwrite: true)`).
   - **`MoveOrRenameTargetStrategy`:** Zieldatei in `B` wird mit Backup-Suffix versehen (`{name}_backup_{timestamp}{ext}`), danach wird die Quelldatei an den regulären Zielort geschoben.
   - **`RenameSourceStrategy`:** Quelldatei wird umbenannt (Vorschlag `{name}_from_source{ext}`) und ins Ziel verschoben; Original-Zieldatei bleibt unberührt.
   - **`SkipConflictStrategy`:** Datei verbleibt in Quelle `A` (No-Op).

### 4.3 Phase 3: Directory Cleanup
Nach Abschluss der Konfliktlösung ruft der Orchestrator `_fileSystem.RemoveEmptyDirectories(sourceRoot)` auf:
- Rekursiver Bottom-Up-Durchlauf durch `Source`.
- Löscht Verzeichnisse nur, wenn sie vollständig leer sind.
- Fehler bei geschützten Dateien (z. B. `desktop.ini`) führen nicht zum Abbruch.

---

## 5. Infrastructure Layer

### 5.1 `WindowsFileSystemService`
- **ReadOnly-Schutz:** Vor jedem `MoveFile` mit `overwrite: true` prüfen, ob Zieldatei `FileAttributes.ReadOnly` oder `FileAttributes.Hidden` hat. Falls ja: Attribute entfernen.
- **Cross-Volume Zeitstempel-Konservierung:**
  - Zeitstempel (`CreationTimeUtc`, `LastWriteTimeUtc`) vor dem `File.Move` erfassen.
  - Nach erfolgreichem `File.Move` auf der Zieldatei explizit wiederherstellen (in sicherem `try/catch`).
- **Simulationsmodus (`IsDryRun`):** Führt Datei- und Verzeichnisänderungen nicht physisch aus, sondern protokolliert die Aktionen.

### 5.2 `GitHubUpdateCheckService`
- Ruft per `HttpClient` die GitHub API auf: `https://api.github.com/repos/mrambow/smart-file-merger/releases/latest`.
- Timeout: 1.5 Sekunden.
- Vergleicht `tag_name` mit aktueller Assembly-Version.
- Fehler (z. B. Offline) werden still gefangen (`HasUpdate = false`).

---

## 6. Presentation Layer: Spectre.Console UI

- **Header:** Figlet-Schriftzug `SmartFileMerger` in Cyan1.
- **Update Notice:** Dezente Box falls Update verfügbar.
- **Wizard Prompts:**
  - Eingabe Quelle (mit Pfadbereinigung).
  - Eingabe Ziel (mit Neuanlage-Bestätigung).
  - Modus-Wahl (`Normal` vs. `Dry-Run / Simulation`).
  - Sicherheitsbestätigung vor Start (`Bist du sicher? [j/N]`).
- **Progress:** Live-Fortschrittsbalken mit verbleibender Zeit und Dateizähler.
- **Konflikt-Panel & Tabelle:**
  - Farbige Gegenüberstellung:
    - Größere / Neuere Datei in `[green]`.
    - Kleinere / Ältere Datei in `[grey]`.
    - Differenztext (z. B. `+240 KB (Quelle größer)`, `2 Tage neuer`).
- **Abschluss-Summary:** Schöne Tabelle mit allen Metriken und Gesamtlaufzeit.

---

## 7. Public Open Source & CI/CD

### 7.1 Repository-Dateien
- **`README.md`:** Klare Projektbeschreibung, Feature-Highlights, Screenshots/ASCII-Previews, Installations- & Verwendungsanleitung.
- **`AGENTS.md`:** Spezifische Richtlinien für KI-Assistenten (Ponytail-Prinzip, Record-Typisierung, keine direkten I/O-Aufrufe außerhalb von Infrastructure, Testabdeckung).
- **`LICENSE`:** Offizielle MIT-Lizenz auf Marc Rambow.

### 7.2 Maker-Checker Workflow (`.github/workflows/claude-pr-gate.yml`)
- Trigger über `pull_request_target` mit Event `labeled`.
- Ausführung nur, wenn Label `review` gesetzt ist **und** der auslösende Sender `mrambow` ist.
- Läuft isoliert mit Workflow-Dateien aus `main` – PR-Zweige können den Review-Gatekeeper nicht manipulieren.
- Verifiziert `AGENTS.md` und führt vorab `dotnet test` aus.

---

## 8. Verifikations- & Testkonzept

- **Unit Tests (`SmartFileMerger.Tests`):**
  - Reine Logik ohne I/O: `DirectoryRoot`, `RelativeFilePath`, `FileConflict`.
  - Gemockte Workflows via NSubstitute: `MergeOrchestrator`, alle 4 Strategien.
  - Abdeckung von Sonderfällen: Identische Dateien, gesperrte Dateien, Ordner-Verschachtelungen.
- **Integration Tests:**
  - Ausführung von `WindowsFileSystemService` auf realen Testdateien im temporären Windows-Verzeichnis (`Path.GetTempPath()`).
  - Test von ReadOnly-Überschreibung und Zeitstempel-Konservierung.
