# Soundboard für Windows

Ein Soundboard für Windows 11: Sounds als Pads, per Klick oder globaler Taste abspielen,
selbst mithören – und zusammen mit dem eigenen Mikrofon über ein virtuelles Audiokabel in
Discord schicken.

Grundsatz: **Die Windows-Soundeinstellungen werden nicht verändert.** Das Programm liest
Standardgeräte nur, öffnet Geräte ausschließlich per Endpunkt-ID im Shared Mode (nie
exklusiv) und stellt weder Lautstärken noch Formate um. Die einzige Ausnahme ist der Knopf
„Standardgerät zurückstellen“ (bzw. `--restore`): Er stellt auf ausdrücklichen Wunsch die
Windows-Standardgeräte auf die vorher gesicherten zurück – nur diese, nie das Kabel. Das
ganze Konzept steht in [KONZEPT.md](KONZEPT.md).

## Stand

Alle acht Schritte des Bauplans (KONZEPT.md, Abschnitt 6.2) sind umgesetzt, Version 0.13.2 mit
überarbeiteter Oberfläche (Pads, Einstellungen mit Reitern, eigene Akzentfarbe per Farbwähler),
Bildern als Pad-Hintergrund, englischer Oberfläche, umschaltbar auf Deutsch, und
eigenem App-Symbol (Taste mit Wellenform, auch im Infobereich immer in Fast-Schwarz #222222).
Getestet ist der Code mit automatischen Tests ohne Hardware; der Test am echten Rechner mit
Kabel und Discord steht aus. Der Installer wird gebaut, Installieren und Deinstallieren sind
**noch ungetestet** (siehe unten).

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
„Einstellungen“ (Zahnrad) → „Einrichtung“ erreichbar. Fehlt das Kabel, steht in der Statuszeile
unten links „Kabel fehlt“ mit Warnzeichen; ein Klick darauf öffnet die Einrichtung. (Die Namen
der Knöpfe stehen hier auf Deutsch; in der englischen Oberfläche heißen sie entsprechend, z. B.
„Settings“ → „Setup“.)

### 1. Virtuelles Kabel (VB-CABLE)

1. Im Soundboard **„Audio-Zustand jetzt sichern“** klicken – so lässt sich später zeigen, ob
   das Treiber-Setup etwas verstellt hat.
2. Auf [vb-cable.com](https://vb-cable.com) das ZIP-Paket für Windows laden und entpacken.
3. `VBCABLE_Setup_x64.exe` mit Rechtsklick → „Als Administrator ausführen“ starten, dann
   „Install Driver“.
4. Windows neu starten, Soundboard öffnen. Es findet „CABLE Input“ und „CABLE Output“ und
   vergleicht mit der Sicherung. Hat das Setup ein Standardgerät verstellt (Windows macht
   neue Audiotreiber manchmal zum Standard), erscheint ein Hinweis über den Pads; „Ansehen“ öffnet den Vergleich mit
   **„Standardgerät zurückstellen“**.
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

### Das Fenster

```
┌──────────────────────────────────────────────────────────────────────────────────┐
│ (●) Mithören [Kopfhörer  v]  ◁) ━━━●── 80 %  [Sounds hinzufügen …] [⛶] [⚙]      [■ Stop] │
├──────────────────────────────────────────────────────────────────────────────────┤
│ ⚠ Ein Windows-Standardgerät weicht von der Sicherung ab …   [Ansehen] [Schließen] │
├──────────────────────────────────────────────────────────────────────────────────┤
│ ┌Num 1───1,2 s┐ ┌Num 2──■ 3,5 s┐ ┌Num 3───6,0 s┐ ┌⚠ Num 3─1,8 s┐ ┌────────12,4 s┐ │
│ │             │ │▓▓▓▓▓ leuchtet│ │             │ │             │ │              │ │
│ │ Airhorn     │ │Trommelwirbel │ │ Applaus 85 %│ │ Ba-dum-tss  │ │ Grillen      │ │
│ └─────────────┘ └──────────────┘ └─────────────┘ └─────────────┘ └──────────────┘ │
├──────────────────────────────────────────────────────────────────────────────────┤
│ Kabel: sendet ▬▬▭   Mikrofon: an   Tasten: 6 aktiv                  Sound-Ordner │
└──────────────────────────────────────────────────────────────────────────────────┘
```

- **Pads:** Jeder Sound ist ein Pad. Klick spielt ab, nochmal klicken stoppt. Ein spielendes
  Pad leuchtet in seiner Farbe auf; der dunklere Teil wandert von links nach rechts und zeigt,
  wie weit der Sound ist. Oben links steht die Taste als Tastenkappe, oben rechts die Länge.
  Eine Lautstärke unter 100 % steht unten rechts.
- **Rechtsklick auf ein Pad:** Taste belegen oder entfernen, Lautstärke (Regler), umbenennen,
  Bild wählen oder entfernen, Bilder-Ordner öffnen, im Sound-Ordner zeigen, Pad entfernen (mit „Rückgängig“).
  **Strg + Mausrad** über einem Pad ändert seine Lautstärke in 5-%-Schritten.
- **Bild als Hintergrund:** Rechtsklick → „Bild wählen …“ oder eine Bilddatei direkt **auf ein
  Pad** ziehen (PNG, JPG, GIF – davon das erste Bild –, BMP, WebP, sofern Windows WebP lesen
  kann). Das Bild füllt das Pad, wird mittig zugeschnitten und ist im Ruhezustand leicht
  abgedunkelt; unten liegt ein dunkler Verlauf, damit Name, Lautstärke und Länge auf jedem Bild
  lesbar bleiben (auch auf reinem Weiß mindestens 4,5 : 1). Beim Abspielen wird das Bild voll
  hell, der Rand leuchtet in der Pad-Farbe, und ein Balken unten zeigt, wie weit der Sound
  ist. Das Bild wird in den Bilder-Ordner kopiert (das Original bleibt) und verkleinert
  geladen, große Fotos brauchen also wenig Speicher. Ein Bild auf der freien Fläche wird kein
  Pad, sondern bekommt einen Hinweis. Lässt sich ein gewähltes oder gezogenes Bild nicht lesen
  (kaputt, falsche Endung, WebP ohne Decoder), sagt ein Hinweis warum, und das Pad
  bleibt, wie es war. Fehlt eine Bilddatei später oder ist sie kaputt, zeigt das Pad ohne
  Warnung still seine Farbe und spielt wie gewohnt; der Verweis bleibt gespeichert, kommt die
  Datei zurück, ist auch das Bild wieder da. „Bild entfernen“ und das Ersetzen eines Bildes
  lassen sich rückgängig machen. Rechtsklick → „Bilder-Ordner öffnen“ zeigt den Ordner mit den
  Pad-Bildern im Explorer (das Bild des Pads ist markiert, falls vorhanden).
- **Sounds hinzufügen:** MP3, WAV, OGG oder FLAC ins Fenster ziehen oder „Sounds hinzufügen …“.
  Die Dateien werden in den Sound-Ordner kopiert, das Original bleibt. Ohne Sounds zeigt das
  Fenster eine Ablagefläche mit demselben Knopf.
- **Globale Tasten:** Klick auf die Tastenkappe (oder Rechtsklick → „Taste belegen …“), dann die
  Taste drücken; Enter nimmt den Vorschlag (Num 1 … Num 9, Num 0). Die Tasten wirken überall,
  auch im Spiel – das Spiel sieht sie dann nicht. **Pause** stoppt alles. Ist eine Taste doppelt
  vergeben oder von einem anderen Programm belegt, trägt ihre Kappe ein Warnzeichen; ein
  Klick darauf zeigt auf dem Pad, was los ist, mit dem Ausweg („Num 3 hierher holen“, „Andere
  Taste wählen“). Numpad-Tasten liefern nur bei eingeschaltetem NumLock „Num 1“ usw.
- **Mithören:** Schalter und Gerät, auf dem du die Sounds selbst hörst (per ID gemerkt).
- **Stop:** der einzige rote Knopf; stoppt alle laufenden Sounds.
- **Regler** (Lautstärke aller Sounds, Mikro-Lautstärke, Pad-Lautstärke im Rechtsklick-Menü):
  Ein Klick auf eine Stelle der Leiste setzt den Wert dort, und ohne loszulassen lässt sich
  gleich weiterziehen. Mit der Tastatur: Pfeiltasten (1 %), Bild auf/ab (10 %).
- **Statuszeile:** Kabel (mit Pegel), Mikrofon, Tasten – ein Klick öffnet die passende
  Einstellungsseite. Ein Warnzeichen erscheint, wenn das Kabel nicht sendet oder eine Taste
  nicht wirkt. Fehlt das Kabel ganz, steht dort fett „Kabel fehlt“, und der Klick öffnet die
  Einrichtung; ein eigener Hinweis über den Pads erscheint dafür nicht. Die
  Windows-Standardgeräte stehen hier nicht; die zeigt Windows selbst.
- **Hinweise:** Es steht immer höchstens ein Hinweis über den Pads – ein Feld wie die
  anderen, erkennbar am bernsteinfarbenen Warnzeichen. Gibt es mehrere,
  blättert „Nächster“ weiter.
- **Fenster schließen** lässt das Programm im Infobereich (Soundboard-Symbol: Taste mit
  Wellenform) weiterlaufen, damit Discord weiter dein Mikrofon bekommt. **Beenden** über Rechtsklick auf das Symbol; die
  Rückfrage sagt, dass Discord dann Stille vom Kabel hört, und zeigt den Vergleich mit der
  Sicherung.
- **Mit Windows starten:** Schalter „Mit Windows starten (nur im Infobereich)“ unter
  „Darstellung“ → „Start“, für die portable EXE genauso wie für die installierte. Er setzt den
  Wert `Soundboard` unter `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` auf
  `"<Pfad dieser EXE>" --tray` (ohne Adminrechte) – derselbe Eintrag wie die Option im
  Installer, der Schalter zeigt also auch einen dort gesetzten an. Beim Anmelden erscheint nur
  das Symbol im Infobereich, kein Fenster; die Einrichtung beim ersten Start kommt erst, wenn
  du das Fenster öffnest. Startet der Eintrag eine andere Kopie (EXE verschoben, alte portable
  Kopie), steht der Schalter auf an, und darunter steht, welche Datei er startet, mit
  „Auf diese Kopie umstellen“. Verweigert Windows die Änderung (Richtlinie, Rechte), springt
  der Schalter zurück und ein Hinweis nennt den Grund.
- **Absturz:** Ein kleiner Wächter-Prozess merkt, wenn das Programm unerwartet endet, und
  meldet, was das für Discord heißt – mit „Neu starten“ und, falls nötig, „Standardgerät
  zurückstellen“.

### Einstellungen (Zahnrad)

Die Einstellungen legen sich über die Pads; Kopfzeile und „Stop“ bleiben. Esc oder
„Schließen“ führt zurück.

| Reiter | Inhalt |
|---|---|
| Einrichtung | die drei Schritte: Kabel, Discord, Rauschunterdrückung (siehe oben) |
| Mikrofon & Kabel | ans Kabel senden; Mikrofon; Mikro-Lautstärke (wirkt nur im Soundboard, nicht in Windows); Pegel und Testton; Puffer klein/mittel/groß (größer wählen, wenn die Stimme knackt); Stimme stumm, solange ein Sound läuft; 10-Sekunden-Test als WAV im Ordner `Aufnahmen` |
| Tasten | Tasten pausieren; alle Pads mit Taste und Zustand, Ausweg bei Konflikten; Sounds überlagern (aus: ein neuer Sound stoppt den laufenden) |
| Audio-Zustand | Vergleich „gesichert – jetzt“ für Standardgeräte, Geräte, Lautstärken; „Standardgerät zurückstellen“, „Audio-Zustand jetzt sichern“, „Neu vergleichen“; darunter die Audiogeräte in Windows (nur angezeigt) |
| Darstellung | Sprache (English, Deutsch); hell oder dunkel (wie Windows, hell, dunkel); Akzentfarbe; Pad-Farben (bunt oder alle in der Akzentfarbe); weiches Aufleuchten an/aus; mit Windows starten (nur im Infobereich) |

**Akzentfarbe:** sechs vertraute Farben (Blau, Petrol, Grün, Orange, Pink, Violett) oder eine
eigene. „Eigene“ öffnet einen Farbwähler: links eine Fläche für Sättigung (nach rechts) und
Helligkeit (nach oben), rechts eine Leiste für den Farbton. Beide lassen sich mit der Maus
ziehen oder mit den Pfeiltasten bewegen (Umschalt: größere Schritte); die Farbe gilt sofort.
Daneben stehen eine Vorschau und, klein, der Hex-Wert zum Abschreiben oder Eintippen. Jede
Farbe wird für hell und dunkel getrennt abgestimmt, bis Schrift darauf und die Farbe auf dem
Grund mindestens 4,5 : 1 Kontrast haben. Muss eine eigene Farbe dafür abgedunkelt oder
aufgehellt werden, sagt eine leise Zeile unter dem Farbwähler, zu welchem Wert. Die
Windows-Akzentfarbe wird nicht benutzt.

**Pad-Farben:** neue Pads bekommen der Reihe nach Blau, Orange, Grün, Pink, Petrol, Violett und
Grau. Profile aus Version 0.10 werden beim ersten Start umgestellt: Die alten Farben nach Sanzo
Wada wandern auf die nächste neue (Pfauenblau → Petrol, Olympiablau → Blau, Blauviolett →
Violett, Purpur → Pink, Olivgelb und Grün → Grün, Schiefer → Blau bzw. bei Pads Grau); eine
eigene Akzentfarbe und selbst gewählte Pad-Farben bleiben.

**Animationen:** Nur das Aufleuchten eines Pads blendet kurz ein (160 ms). Ist es im Reiter
„Darstellung“ aus oder sind in Windows die Animationseffekte ausgeschaltet, leuchtet das Pad sofort
auf. Was per Taste
ausgelöst wird, bewegt sich sonst nicht.

Fehlt etwas (kein Mikrofon, Taste belegt), erscheint ein Hinweis mit Warnzeichen und dem nächsten
Schritt – bei Pads direkt auf dem Pad. Ein fehlendes Kabel zeigt nur die Statuszeile (siehe
oben). Knöpfe werden nie ausgegraut.

## Sprache

Die Oberfläche ist auf Englisch und lässt sich unter Einstellungen → „Appearance“ →
„Language“ auf Deutsch umstellen (und unter „Darstellung“ → „Sprache“ zurück). Der Wechsel gilt
sofort, ohne Neustart: Fenster, Pads, Statuszeile, Hinweise, Tooltips, Infobereich-Menü,
Dateidialoge, Wächter- und Rückbau-Fenster. Zahlen und Datum folgen der Sprache (Englisch
„80%“, „4 Nov 2026“; Deutsch „80 %“, „04.11.2026“). Gespeichert wird die Wahl in `config.json`.
Neue Installationen starten auf Englisch; wer schon eine Version bis 0.10 benutzt hat, behält
Deutsch. Die Kommandozeile (`--check`, `--restore`, `--help`) spricht die gespeicherte Sprache,
ohne Einstellungen Englisch.

Alle Texte stehen in `src/Soundboard.Core/Text/En.resx` und `De.resx` unter demselben
Schlüssel. Ein Test findet Schlüssel, die in einer Sprache fehlen, und solche, die im Code
benutzt, aber nirgends definiert sind. Die Ordnernamen im Datenordner (`Sounds`, `Bilder`,
`Aufnahmen`) bleiben in beiden Sprachen gleich.

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

Rechtsklick auf das Soundboard-Symbol im Infobereich → **„Alles entfernen …“** oder `Soundboard.exe --uninstall`:

1. **Audio-Zustand prüfen**, bei Abweichung „Standardgerät zurückstellen“.
2. **Eigene Daten löschen** – erst nach Rückfrage; „Sounds behalten“ ist vorausgewählt und
   behält auch die Pad-Bilder. Entfernt werden Einstellungen, Sicherungen, Test-Aufnahmen und
   der Autostart-Eintrag, falls er diese EXE startet (einer für eine andere Kopie bleibt); ohne Häkchen auch Sound- und Bilder-Ordner.
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
| `config.json` | Pads (mit Bild), Tasten, Geräte-IDs, Lautstärken, Puffer, Sprache, Darstellung (hell/dunkel, Akzentfarbe, Pad-Farben, Animationen) |
| `Sounds\` | die hereingezogenen Sounds (Kopien) |
| `Bilder\` | die Pad-Bilder (Kopien); ein entferntes oder ersetztes Bild wird beim Beenden gelöscht, sofern es kein Pad mehr nutzt. Andere Dateien im Ordner bleiben unberührt |
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

### App-Symbol

Das Symbol wird im Code gezeichnet (`src/Soundboard.App/Icons/AppIconDrawing.cs`): 16, 20 und
24 px pixelgenau, die übrigen Größen aus einer Formel. Die EXE-, Fenster- und Installer-Datei
`src/Soundboard.App/Assets/Soundboard.ico` (Fast-Schwarz #222222, 16/20/24/32/48/256 px, PNG-Einträge) ist
eingecheckt. Nach einer Änderung an der Zeichnung neu erzeugen:

```powershell
dotnet run --project tools/IconGen
```

Ein Test vergleicht die eingecheckte Datei mit der Zeichnung und schlägt fehl, wenn sie veraltet
ist. Das Symbol im Infobereich zeichnet die App zur Laufzeit selbst, immer in Fast-Schwarz #222222 wie das
App-Symbol (unabhängig von der Akzentfarbe) und in der Größe, die Windows für die Skalierung der
Taskleiste vorsieht.

## Installer (gebaut, Installation noch ungetestet)

`installer/soundboard.iss` ist ein Skript für [Inno Setup 6](https://jrsoftware.org/isinfo.php):
Installation nur für den eigenen Benutzer nach `%LOCALAPPDATA%\Programs\Soundboard`, ohne
Adminrechte, Startmenü-Eintrag (mit dem Symbol der EXE), „Mit Windows starten“ als abwählbare
Option (derselbe Run-Wert wie der Schalter im Programm; die Deinstallation entfernt ihn). Der Installer spricht Englisch, auf einem deutschen Windows Deutsch. Beim
Deinstallieren startet die Anleitung `--uninstall`; bei stiller Deinstallation nur
`--restore`. Das Skript kompiliert mit Inno Setup 6.7 ohne Warnungen; Installieren und
Deinstallieren sind **noch nicht ausprobiert**. Bauen nach dem Publish:

```powershell
iscc installer\soundboard.iss
```

Ergebnis: `publish\installer\Soundboard-Setup-<Version>.exe`. Inno Setup lässt sich ohne
Adminrechte installieren: `winget install JRSoftware.InnoSetup --scope user --custom /CURRENTUSER`.

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
