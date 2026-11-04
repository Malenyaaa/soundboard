# Soundboard für Windows

Ein Soundboard für Windows 11: Sounds als Kacheln, per Klick oder globaler Taste abspielen,
selbst mithören – und zusammen mit dem eigenen Mikrofon über ein virtuelles Audiokabel in
Discord schicken.

Grundsatz: **Die Windows-Soundeinstellungen werden nicht verändert.** Das Programm liest
Standardgeräte nur, öffnet Geräte ausschließlich per Endpunkt-ID im Shared Mode (nie
exklusiv) und stellt weder Lautstärken noch Formate um. Die einzige Ausnahme ist der Knopf
„Wie vorher“ (bzw. `--restore`): Er stellt auf ausdrücklichen Wunsch die
Windows-Standardgeräte auf die vorher gesicherten zurück – nur diese, nie das Kabel. Das
ganze Konzept steht in [KONZEPT.md](KONZEPT.md).

## Stand

Alle acht Schritte des Bauplans (KONZEPT.md, Abschnitt 6.2) sind umgesetzt, Version 0.8.0.
Getestet ist der Code mit automatischen Tests ohne Hardware; der Test am echten Rechner mit
Kabel und Discord steht aus. Der Installer ist **noch ungetestet** (siehe unten).

## So funktioniert es

```
Mikrofon (per ID, Shared Mode) ─┐
                                ├─ Mixer ─► „CABLE Input“ ─► Discord nimmt „CABLE Output“ als Mikrofon
Sounds ─────────────────────────┤
                                └─ nur Sounds ─► Kopfhörer („Mithören“)
```

Nur Discord stellt in seinen eigenen Einstellungen auf das Kabel um. Windows behält sein
Standardmikrofon und seine Standardwiedergabe.

## Einrichtung

Die Einrichtung öffnet sich beim ersten Start und ist jederzeit über den Knopf
„Einrichtung“ erreichbar.

### 1. Virtuelles Kabel (VB-CABLE)

1. Im Soundboard **„Audio-Zustand jetzt sichern“** klicken – so lässt sich später zeigen, ob
   das Treiber-Setup etwas verstellt hat.
2. Auf [vb-cable.com](https://vb-cable.com) das ZIP-Paket für Windows laden und entpacken.
3. `VBCABLE_Setup_x64.exe` mit Rechtsklick → „Als Administrator ausführen“ starten, dann
   „Install Driver“.
4. Windows neu starten, Soundboard öffnen. Es findet „CABLE Input“ und „CABLE Output“ und
   vergleicht mit der Sicherung. Hat das Setup ein Standardgerät verstellt (Windows macht
   neue Audiotreiber manchmal zum Standard), erscheint ein gelber Hinweis mit **„Wie
   vorher“**.
5. Prüfen: Beim Sprechen schlägt „Das sendet dein Kabel gerade“ aus. Gegenprobe ohne
   Discord: Systemsteuerung → Sound → Registerkarte „Aufnahme“ → bei „CABLE Output“
   bewegt sich die Anzeige. „Testton senden“ schickt eine Sekunde 440 Hz nur ins Kabel.

### 2. Discord

1. Discord → Benutzereinstellungen (Zahnrad) → **Sprache & Video**.
2. Eingabegerät: **„CABLE Output (VB-Audio Virtual Cable)“**.
3. Ausgabegerät **nicht ändern**.
4. Vorher aufschreiben, welches Mikrofon Discord hatte – das Feld dafür steht in der
   Einrichtung. Es wird für den Rückweg angezeigt.

### 3. Rauschunterdrückung

Discords Rauschunterdrückung hält Soundboard-Sounds für Störgeräusche. In Discord unter
Sprache & Video:

- Rauschunterdrückung: **Keine** (oder „Standard“)
- Echounterdrückung: **aus**
- Automatische Verstärkung: **aus**

**Push-to-Talk:** Discord behandelt das Kabel wie ein Mikrofon. Mit Push-to-Talk hören die
anderen Sounds nur, solange die Taste gehalten wird. Angenehmer ist Sprachaktivierung oder
eine zweite Push-to-Talk-Taste in Discord.

Das Soundboard kann diese Discord-Einstellungen weder lesen noch ändern; es schreibt nicht in
Discords Dateien.

## Bedienung

- **Sounds hinzufügen:** MP3, WAV, OGG oder FLAC ins Fenster ziehen oder „Sounds hinzufügen …“.
  Die Dateien werden in den Sound-Ordner kopiert, das Original bleibt.
- **Abspielen:** Kachel anklicken, nochmal klicken stoppt. Rechtsklick: Taste belegen,
  umbenennen, im Ordner zeigen, entfernen (mit „Rückgängig“).
- **Globale Tasten:** Rechtsklick → „Taste belegen …“, dann die Taste drücken; Enter nimmt den
  Vorschlag (Num 1 … Num 9, Num 0). Die Tasten wirken überall, auch im Spiel – das Spiel sieht
  sie dann nicht. **Pause** stoppt alles. Ist eine Taste doppelt vergeben oder von einem
  anderen Programm belegt, steht das gelb da, mit einem Ausweg. „Hotkeys pausieren“ gibt alle
  Tasten frei. Hinweis: Numpad-Tasten liefern nur bei eingeschaltetem NumLock „Num 1“ usw.
- **Mithören auf:** Gerät, auf dem du die Sounds selbst hörst (per ID gemerkt).
- **Mikrofon, Mikro-Lautstärke, Pegel:** das echte Mikrofon für den Mix; die Lautstärke wirkt
  nur im Soundboard, nicht in Windows.
- **Ans Kabel senden:** Mikrofon und Sounds gehen ins Kabel, solange das Häkchen gesetzt ist.
- **Puffer klein/mittel/groß:** größer wählen, wenn die Stimme knackt. Die Statusleiste zählt
  Aussetzer.
- **Stimme blockieren:** Mikrofon ist stumm, solange ein Sound läuft (Standard aus).
- **10 s Test aufnehmen:** nimmt Mikrofon und Sounds zehn Sekunden lang in eine WAV-Datei auf –
  zum Anhören ohne Kabel und ohne Discord. Die Datei liegt im Ordner `Aufnahmen`.
- **Audio-Zustand** (unten aufklappen): Vergleich „gesichert – jetzt“ für Standardgeräte,
  Geräte, Lautstärken. Knöpfe „Wie vorher“, „Audio-Zustand jetzt sichern“, „Neu vergleichen“.
- **Fenster schließen** lässt das Programm im Infobereich (Notensymbol) weiterlaufen, damit
  Discord weiter dein Mikrofon bekommt. **Beenden** über Rechtsklick auf das Symbol; die
  Rückfrage sagt, dass Discord dann Stille vom Kabel hört, und zeigt den Vergleich mit der
  Sicherung.
- **Absturz:** Ein kleiner Wächter-Prozess merkt, wenn das Programm unerwartet endet, und
  meldet, was das für Discord heißt – mit „Neu starten“ und, falls nötig, „Wie vorher“.

Fehlt etwas (kein Kabel, kein Mikrofon, Taste belegt), erscheint ein gelber Hinweis mit dem
nächsten Schritt. Knöpfe werden nie ausgegraut.

## Kommandozeile

```powershell
.\Soundboard.exe --check | Out-String     # Audio-Zustand als Text (nur lesen)
.\Soundboard.exe --restore | Out-String   # Standardgeräte auf die Sicherung zurückstellen
.\Soundboard.exe --uninstall              # Anleitung zum Entfernen
.\Soundboard.exe --tray                   # ohne Fenster starten
```

`--watch <pid>` startet das Programm intern als Wächter. Weil `Soundboard.exe` ein
Fensterprogramm ist, wartet die Konsole nicht von selbst auf die Ausgabe; `| Out-String`
(PowerShell) bzw. `start /wait` (Eingabeaufforderung) sorgt dafür.

`--restore` stellt nur Standardgeräte zurück, nur auf IDs aus der Sicherung, nur wenn das
Gerät angeschlossen und aktiv ist, und nie ein virtuelles Kabel. Was nicht geht, wird mit
dem Weg „von Hand“ ausgegeben.

## Rückbau

Rechtsklick auf das Notensymbol → **„Alles entfernen …“** oder `Soundboard.exe --uninstall`:

1. **Audio-Zustand prüfen**, bei Abweichung „Wie vorher“.
2. **Eigene Daten löschen** – erst nach Rückfrage; „Sounds behalten“ ist vorausgewählt.
   Entfernt werden Einstellungen, Sicherungen, Test-Aufnahmen und der Autostart-Eintrag,
   falls gesetzt.
3. **Kabel entfernen** – das Soundboard deinstalliert keinen Treiber. Weg: VB-CABLE-Setup als
   Administrator → „Remove Driver“ → Neustart → Geräte-Manager, „Ausgeblendete Geräte
   anzeigen“, verbliebene VB-Audio-Einträge entfernen → zweiter Neustart. Bei Resten im
   Treiberspeicher: `pnputil /enum-drivers` und `pnputil /delete-driver oemNN.inf` (als
   Administrator).
4. **Zum Schluss** Vergleich „gesichert – jetzt“ und der Hinweis zu Discord: Zeigt Discord noch
   auf das entfernte Kabel, nimmt es von selbst „Default“, also das Windows-Standardmikrofon,
   das das Soundboard nie verändert hat. Hattest du vorher ein anderes Mikrofon gewählt, steht
   sein Name da.

Danach die portable EXE einfach löschen.

## Daten

Alles liegt unter `%LOCALAPPDATA%\Soundboard\`:

| Datei/Ordner | Inhalt |
|---|---|
| `config.json` | Kacheln, Tasten, Geräte-IDs, Lautstärken, Puffer |
| `Sounds\` | die hereingezogenen Sounds (Kopien) |
| `Aufnahmen\` | 10-Sekunden-Testaufnahmen |
| `audio-snapshot.json` | gesicherter Windows-Audio-Zustand (beim ersten Start automatisch) |
| `audio-snapshot.vorher.json` | die Sicherung davor |
| `running.json` | Merker „läuft gerade“; bleibt er nach dem Ende liegen, war es ein Absturz |

## Bauen

Voraussetzung: .NET 10 SDK.

```powershell
dotnet build Soundboard.sln
dotnet test Soundboard.sln
dotnet publish src/Soundboard.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

Ergebnis: `publish\Soundboard.exe` (portable, eine Datei, keine .NET-Installation nötig).

## Installer (noch ungetestet)

`installer/soundboard.iss` ist ein Skript für [Inno Setup 6](https://jrsoftware.org/isinfo.php):
Installation nur für den eigenen Benutzer nach `%LOCALAPPDATA%\Programs\Soundboard`, ohne
Adminrechte, Startmenü-Eintrag, „Mit Windows starten“ als abwählbare Option. Beim
Deinstallieren startet die Anleitung `--uninstall`; bei stiller Deinstallation nur
`--restore`. Das Skript ist **noch nie kompiliert oder ausprobiert worden**. Bauen nach dem
Publish:

```powershell
iscc installer\soundboard.iss
```

Die portable EXE bleibt der zweite Download.

## Mitarbeit

Vor jedem Commit prüft `tools/pruefen.ps1`, dass keine lokalen Pfade, Namen oder
E-Mail-Adressen ins Repo gelangen. Einmal nach dem Klonen einschalten:

```powershell
git config core.hooksPath .githooks
```

## Lizenz

MIT, © 2026 Malenya – siehe [LICENSE](LICENSE).

Verwendete Bibliotheken (NuGet-Pakete), alle unter MIT-Lizenz: NAudio, NVorbis,
CommunityToolkit.Mvvm, H.NotifyIcon.Wpf (mit H.NotifyIcon und H.GeneratedIcons.System.Drawing).

### VB-CABLE

Das virtuelle Kabel **VB-CABLE** ist ein Produkt von VB-Audio
([vb-cable.com](https://vb-cable.com)) und gehört nicht zu diesem Projekt. Es wird **nicht
mitgeliefert**; das Soundboard verlinkt nur auf die Download-Seite. VB-CABLE ist
Donationware: „VB-CABLE is a donationware, all participations are welcome.“ Wer es nutzt,
kann VB-Audio über deren Seite unterstützen. Lizenzbedingungen:
[VB-Audio Licensing](https://www.vb-audio.com/Services/licensing.htm).
