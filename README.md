# Soundboard für Windows

Ein Soundboard für Windows 11: Sounds als Kacheln, per Klick abspielen und selbst mithören.
Später sollen die Sounds zusammen mit dem eigenen Mikrofon über ein virtuelles Audiokabel in
Discord ankommen. Grundsatz: **Die Windows-Soundeinstellungen werden nie verändert.** Das
Programm liest Standardgeräte nur, öffnet Geräte ausschließlich per Endpunkt-ID im
Shared Mode und stellt weder Lautstärken noch Formate um. Das ganze Konzept steht in
[KONZEPT.md](KONZEPT.md).

## Stand

Schritt 2 von 8 (siehe Bauplan in KONZEPT.md, Abschnitt 6.2).

- **Schritt 1:** Geräteliste aller Audio-Endpunkte mit ID, Status, Lautstärke und
  Standard-Markierung je Rolle (Konsole, Multimedia, Kommunikation) – nur lesend; `--check`
  gibt denselben Zustand als Text aus.
- **Schritt 2:** Kacheln, Drag & Drop (MP3, WAV, OGG, FLAC – die Dateien werden in den
  Sound-Ordner kopiert, das Original bleibt), Mithören auf einem per ID gewählten Gerät,
  „Alles stoppen“, Lautstärke je Kachel und insgesamt, „neuer Sound stoppt alten“ oder
  „überlagern“.

Noch nicht enthalten: Mikrofon, virtuelles Kabel, Hotkeys, Sichern/Zurückstellen des
Audio-Zustands, Tray-Symbol.

## Bedienung

- **Sounds hinzufügen:** Dateien ins Fenster ziehen oder „Sounds hinzufügen …“.
- **Abspielen:** Kachel anklicken; nochmal klicken stoppt. Rechtsklick: umbenennen, im
  Sound-Ordner zeigen, entfernen (mit „Rückgängig“).
- **Mithören auf:** Gerät, auf dem du die Sounds hörst. Beim ersten Start ist das aktuelle
  Windows-Standardgerät vorgewählt; gemerkt wird seine ID. Ändert sich später der
  Windows-Standard, bleibt die Wahl hier, wie sie ist.
- **Geräteliste:** unten aufklappen. Sie zeigt, was Windows meldet, und aktualisiert sich
  selbst, wenn Geräte dazukommen oder wegfallen.

Fehlt etwas (kein Gerät, Datei unlesbar), erscheint ein gelber Hinweis mit dem nächsten
Schritt.

Daten liegen unter `%LOCALAPPDATA%\Soundboard\`: `config.json` (Kacheln, gewähltes Gerät,
Lautstärken) und der Ordner `Sounds`.

## Bauen und starten

Voraussetzung: .NET 10 SDK.

```powershell
dotnet build Soundboard.sln
dotnet test Soundboard.sln
dotnet run --project src/Soundboard.App
```

Eine einzelne EXE ohne installierte .NET-Laufzeit:

```powershell
dotnet publish src/Soundboard.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

Ergebnis: `publish\Soundboard.exe`.

## Audio-Zustand prüfen: `--check`

```powershell
.\Soundboard.exe --check | Out-String
```

Gibt die Standardgeräte je Rolle für Wiedergabe und Aufnahme, alle Endpunkte mit ID und
Status sowie Lautstärke, Stummschaltung und Mix-Format der aktiven Geräte aus. Es wird nur
gelesen. Weil `Soundboard.exe` ein Fensterprogramm ist, wartet die Konsole nicht von selbst
auf die Ausgabe; `| Out-String` (PowerShell) bzw. `start /wait Soundboard.exe --check`
(Eingabeaufforderung) sorgt dafür, dass sie vollständig vor der nächsten Eingabezeile steht.
`--check > zustand.txt` schreibt sie in eine Datei.

## Mitarbeit

Vor jedem Commit prüft `tools/pruefen.ps1`, dass keine lokalen Pfade, Namen oder
E-Mail-Adressen ins Repo gelangen. Einmal nach dem Klonen einschalten:

```powershell
git config core.hooksPath .githooks
```

## Lizenz

MIT, © 2026 Malenya – siehe [LICENSE](LICENSE).

Verwendete Bibliotheken (NuGet-Pakete), alle unter MIT-Lizenz: NAudio, NVorbis,
CommunityToolkit.Mvvm.
Das spätere virtuelle Kabel (VB-CABLE von VB-Audio) gehört nicht zu diesem Projekt und wird
nicht mitgeliefert.
