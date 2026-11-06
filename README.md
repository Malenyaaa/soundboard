# Soundboard für Windows

Ein Soundboard für Windows 11: Sounds als Pads, per Klick oder globaler Taste abspielen,
selbst mithören – und zusammen mit dem eigenen Mikrofon über ein virtuelles Audiokabel in
Discord schicken.

Grundsatz: **Das Soundboard darf Windows-Einstellungen ändern, wenn es die Bedienung einfacher
macht – aber es stellt sie zuverlässig zurück**, sodass Windows ohne das Soundboard wieder normal
klingt (Hintergrund: Audio-Software, die Standardgeräte übernimmt und ohne die dann kein Ton mehr
da ist). Jede Änderung steht mit dem alten Wert im Änderungs-Journal (`changes.json`), **bevor**
Windows geändert wird, und geht zurück beim Beenden, nach einem Absturz über den Wächter, beim
nächsten Start (falls auch der Wächter nicht mehr lief), mit „Alle Änderungen zurücknehmen“, mit
`--restore` und beim Deinstallieren. Was das Soundboard ändert, steht unter
[Was das Soundboard an Windows ändert](#was-das-soundboard-an-windows-ändert). Sonst öffnet es
Geräte nur per Endpunkt-ID im Shared Mode (nie exklusiv) und stellt keine Lautstärken um.
Standardgeräte stellt es nur **zurück**, nie auf etwas Neues, und nie auf das Kabel. Das ganze
Konzept steht in [KONZEPT.md](KONZEPT.md).

## Stand

Alle acht Schritte des Bauplans (KONZEPT.md, Abschnitt 6.2) sind umgesetzt, Version 0.17.0 mit
überarbeiteter Oberfläche (Pads, Einstellungen mit Reitern, eigene Akzentfarbe per Farbwähler),
Bildern als Pad-Hintergrund, englischer Oberfläche, umschaltbar auf Deutsch, und
eigenem App-Symbol (Taste mit Wellenform, auch im Infobereich immer in Fast-Schwarz #222222).
Neu in 0.14: Suche über den Pads, Pads per Ziehen sortieren, Farbe je Pad, frei belegbare
Stopp-Taste, „Mikrofon stumm“ als Taste, Profil als ZIP exportieren/importieren, Lautstärke
angleichen, mehrstufiges Rückgängig. In 0.14.1: Der Audio-Zustand wird im Hintergrund gesichert;
gemeldet wird nur noch als „Audio“ mit Warnzeichen in der Statuszeile, wenn wirklich etwas nicht geht.
In 0.14.2: Das Zahnrad steht ganz rechts neben „Stop“; „Sounds hinzufügen …“ ist aus der Kopfzeile
heraus und steckt im Rechtsklick auf die freie Fläche und auf Strg+O; das Suchfeld heißt „Sounds suchen“.
In 0.14.3: Schalter (etwa „Mithören“) gleiten beim Umschalten, die Farbe blendet mit; mit
ausgeschalteten Animationen springen sie wie bisher. In 0.14.4: Gerätewahl, Suchfeld, „Stop“ und Zahnrad
in der Kopfzeile sind gleich hoch (36 px) und stehen auf einer Linie; das Zahnrad dreht sich beim Darüberfahren
um einen Zahn, die Einstellungen gleiten vom Zahnrad aus auf und wieder zu (mit der Tastatur und mit
ausgeschalteten Animationen sofort). In 0.14.5: Die Einstellungen gehen sofort auf; die Geräteliste
(über eine Sekunde bei vielen alten Geräten) wird danach im Hintergrund gelesen, ebenso nach Geräteänderungen
und beim Zurückholen des Fensters. Das Zahnrad dreht sich bei jedem Darüberfahren um zwei Zähne,
nicht mehr nur beim ersten Mal. In 0.14.6: Das Zahnrad dreht beim Verlassen wieder zurück; sind die
Einstellungen offen, schließt ein Klick aufs Zahnrad sie. Das Zahnrad öffnet immer auf „Mikrofon & Kabel“,
jetzt der erste Reiter. Der Strich unter dem offenen Reiter gleitet beim Wechsel zum neuen Reiter.
In 0.15: Das Soundboard darf Windows-Einstellungen ändern, wenn es die Bedienung einfacher macht, und
stellt sie zuverlässig zurück (Änderungs-Journal, siehe „Was das Soundboard an Windows ändert“): Hat
Windows das Kabel zum Standardgerät gemacht, stellt es still das gesicherte Gerät zurück; das
Windows-Ducking ist aus, solange es läuft; das Kabel läuft mit 48 kHz; der Wächter stellt nach einem
Absturz still zurück und zeigt ein Fenster nur noch, wenn das Kabel lief.
In 0.15.1: Das Mikrofon kommt wieder ins Kabel (der Aufnahme-Thread öffnete das Gerät aus dem
UI-Thread und brach mit „Specified cast is not valid“ ab, sodass nur die Sounds gesendet wurden).
Das Kabel als Standard-Wiedergabegerät wird nie mehr als „normal“ gesichert, auch nicht aus einer
älteren Sicherung; die Rolle bleibt leer, bis dort ein echtes Gerät Standard ist. Das Zahnrad bleibt
gedreht, solange die Einstellungen offen sind, und dreht beim Schließen zurück.
In 0.15.2: Eigener Regler „Sounds für andere“ (Mikrofon & Kabel): Lautstärke der Sounds nur im Kabel,
zusätzlich zur Gesamtlautstärke, voreingestellt auf 35 % – Sounddateien sind laut gemastert, die Stimme
ist viel leiser, und die eigene Windows-Lautstärke wirkt aufs Kabel nicht.
In 0.15.3: Beim Darüberfahren, Klicken, Auswählen und Fokussieren ändert sich keine Größe mehr –
Schrift und Rahmenlinien bleiben stehen (Pads, Auswahlknöpfe, Reiter, Eingabefelder, „Stop“,
Statuszeile); stärkerer Rahmen und Abdunkeln liegen als Ebene darüber. Gelöschte oder verschobene
Pads blenden keinen Hinweis mehr ein; Strg+Z nimmt sie weiter zurück. Pads ohne Taste zeigen beim
Darüberfahren kein „Taste zuweisen“ mehr; zuweisen geht über Rechtsklick → „Taste zuweisen …“.
Im Hauptfenster (Kopfzeile, Pads, Statuszeile, Rechtsklick-Menüs) gehen keine Tooltips mehr auf;
in den Einstellungen bleiben sie.
In 0.16: Rechtsklick auf ein Pad → „Zuschneiden …“: Anfang und Ende in der Wellenform ziehen
(oder in die Welle klicken; Pfeiltasten 10 ms, mit Umschalt 100 ms, Leertaste wechselt den Rand),
„Anhören“ spielt den Ausschnitt nur zum Mithören, nicht ins Kabel. Das Pad merkt sich Anfang und Ende,
die Datei bleibt unverändert; Strg+Z nimmt den Schnitt zurück.
In 0.17: Favoriten – Rechtsklick auf ein Pad → „Favorit“. Favoriten stehen immer vorne in einer
eigenen Reihe (mit etwas Abstand zu den anderen) und tragen unten rechts einen kleinen Stern;
verschieben geht innerhalb der eigenen Gruppe; Strg+Z nimmt es zurück.
In 0.17.1: Gibt es Favoriten, tragen beide Gruppen einen schlichten Kopf – „Favoriten“ und
„Weitere Pads“ in kleiner Schrift mit dünner Linie bis zum rechten Rand.
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

Den Windows-Audio-Zustand sichert das Soundboard selbst im Hintergrund (beim ersten Start und
wenn Geräte dazukommen, etwa das Kabel); dafür muss man nichts tun.

1. Auf [vb-cable.com](https://vb-cable.com) das ZIP-Paket für Windows laden und entpacken.
2. `VBCABLE_Setup_x64.exe` mit Rechtsklick → „Als Administrator ausführen“ starten, dann
   „Install Driver“.
3. Windows neu starten, Soundboard öffnen. Es findet „CABLE Input“ und „CABLE Output“ und
   vergleicht mit der Sicherung. Hat das Setup das Kabel zum Standardgerät gemacht (Windows macht
   neue Audiotreiber manchmal zum Standard), stellt das Soundboard **sofort und ohne Rückfrage** das
   gesicherte Gerät zurück – je Rolle (Konsole, Multimedia, Kommunikation), für Wiedergabe und
   Aufnahme, nur wenn das gesicherte Gerät angeschlossen und aktiv ist. Im Reiter „Audio-Zustand“
   steht danach leise „Windows hatte das Kabel zum Standardgerät gemacht – auf dein Gerät
   zurückgestellt“; ist das Fenster gerade nicht vorn, kommt dazu eine stille Windows-Benachrichtigung.
   Fehlt das gesicherte Gerät, wird nichts umgestellt, und unten in der Statuszeile steht **„Audio“
   mit Warnzeichen**; ein Klick öffnet den Reiter mit **„Standardgerät zurückstellen“**.
4. Prüfen: Beim Sprechen schlägt „Das sendet dein Kabel gerade“ aus. Gegenprobe ohne
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
│ (●) Mithören [Kopfhörer v]  ◁) ━━●── 80 %      [Sounds suchen …]    [■ Stop] [⚙] │
├──────────────────────────────────────────────────────────────────────────────────┤
│ ⚠ Nicht übernommen: notiz.txt. Möglich sind MP3, WAV, OGG und FLAC.      [Schließen] │
├──────────────────────────────────────────────────────────────────────────────────┤
│ ┌Num 1───1,2 s┐ ┌Num 2─■ −2,0 s┐ ┌Num 3───6,0 s┐ ┌⚠ Num 3─1,8 s┐ ┌────────12,4 s┐ │
│ │             │ │▓▓▓▓▓ leuchtet│ │             │ │             │ │              │ │
│ │ Airhorn     │ │Trommelwirbel │ │ Applaus 85 %│ │ Ba-dum-tss  │ │ Grillen      │ │
│ └▁▁▁▁▁▁▁▁▁▁▁▁▁┘ └▁▁▁▁▁▁▁▁▁▁▁▁▁▁┘ └▁▁▁▁▁▁▁▁▁▁▁▁▁┘ └▁▁▁▁▁▁▁▁▁▁▁▁▁┘ └▁▁▁▁▁▁▁▁▁▁▁▁▁▁┘ │
├──────────────────────────────────────────────────────────────────────────────────┤
│ Kabel: sendet ▬▬▭   Mikrofon: an   Tasten: 6 aktiv                  Sound-Ordner │
└──────────────────────────────────────────────────────────────────────────────────┘
```

Ist das Fenster schmaler, rücken Lautstärke und Suche in eine zweite Zeile; Mithören, Stop und
das Zahnrad bleiben oben. Die Tab-Taste geht die Kopfzeile in dieser Lesereihenfolge durch.

- **Pads:** Jeder Sound ist ein Pad – eine Taste mit dicker Unterkante, gedrückt sinkt sie ein.
  Klick spielt ab, nochmal klicken stoppt. Ein spielendes
  Pad leuchtet in seiner Farbe auf; der dunklere Teil wandert von links nach rechts und zeigt,
  wie weit der Sound ist. Oben links steht die Taste als Tastenkappe, oben rechts die Länge,
  während des Abspielens die Restzeit („−2,1 s“). Ist eine Taste zu lang für das Pad, weicht
  zuerst die Länge; reicht es dann noch nicht, fallen die Umschalttasten von vorne weg
  („…Umschalt+F12“) – die Taste selbst wird nie gekürzt, die volle Belegung steht im Tooltip.
  Eine Lautstärke unter 100 % steht unten rechts.
- **Rechtsklick auf ein Pad:** Taste belegen oder entfernen, Lautstärke (Regler), „Lautstärke
  angleichen“ / „Alle Pads angleichen“, umbenennen (F2), „Farbe ▸“ (automatisch nach
  Reihenfolge oder eine der sieben Pad-Farben), „Bild ▸“ (wählen, entfernen, Bilder-Ordner
  öffnen), im Sound-Ordner zeigen, Pad entfernen (Entf, mit „Rückgängig“).
  **Strg + Mausrad** über einem Pad ändert seine Lautstärke in 5-%-Schritten.
- **Tastatur im Pad-Raster:** Tab springt einmal ins Raster, die Pfeiltasten wandern zwischen
  den Pads, Leertaste/Enter spielt, F2 benennt um, Entf entfernt, Umschalt+F10 öffnet das
  Rechtsklick-Menü. Bildschirmleser hören „spielt“ als Zustand.
- **Sortieren:** Ein Pad mit der Maus auf eine andere Stelle ziehen (das Ziehen beginnt erst
  nach ein paar Pixeln, ein Klick bleibt ein Klick); ein Balken zeigt die Einfügestelle, Esc
  bricht ab. Mit der Tastatur verschiebt **Strg + Pfeil links/rechts** das fokussierte Pad.
  Automatische Farben folgen dem Platz.
- **Suche:** Das Feld „Sounds suchen“ steht immer in der Kopfzeile; **Strg+F** springt hinein,
  Esc leert es. Gesucht wird in Name und Taste („air“, „num 3“), jedes Wort muss passen. Ohne
  Treffer steht dort ein Satz mit „Suche leeren“.
- **Rückgängig:** Entfernen, Verschieben, Angleichen und Importieren landen auf einem Stapel.
  Der Hinweis nennt den neuesten Schritt und wie viele danach noch zurückgehen; **Strg+Z**
  oder „Rückgängig“ nimmt einen nach dem anderen zurück.
- **Lautstärke angleichen:** misst die dekodierten Sounds (Spitze und RMS ohne Stille) und
  stellt die Pad-Lautstärke so, dass laute Pads so laut klingen wie das mittlere. Leisere
  bleiben auf 100 %, die Dateien bleiben unverändert.
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
- **Sounds hinzufügen:** MP3, WAV, OGG oder FLAC ins Fenster ziehen, **Strg+O** drücken oder
  Rechtsklick auf die freie Fläche zwischen und unter den Pads → „Sounds hinzufügen …“.
  Die Dateien werden in den Sound-Ordner kopiert, das Original bleibt. Danach rollt das
  Fenster zum ersten neuen Pad, die neuen Pads tragen kurz einen Ring. Ohne Sounds zeigt das
  Fenster eine Ablagefläche mit demselben Knopf.
- **Globale Tasten:** Klick auf die Tastenkappe (oder Rechtsklick → „Taste belegen …“), dann die
  Taste drücken; Enter nimmt den angezeigten Vorschlag. Vorgeschlagen werden
  Strg+Alt+Umschalt+1 … 0 – Num 1 … Num 0 vorher nur, wenn in dieser Sitzung schon eine
  Numpad-Taste gedrückt wurde (ein Laptop ohne Numpad bekommt sonst Tasten, die es nicht gibt).
  Nie Strg+Alt+Ziffer allein: Auf deutscher Tastatur ist das AltGr+Ziffer, und { [ ] } ² ³ ließen
  sich sonst nirgends mehr tippen, solange das Soundboard läuft.
  Die Tasten wirken überall,
  auch im Spiel – das Spiel sieht sie dann nicht. Ist eine Taste doppelt
  vergeben oder von einem anderen Programm belegt, trägt ihre Kappe ein Warnzeichen; ein
  Klick darauf zeigt auf dem Pad, was los ist, mit dem Ausweg („Num 3 hierher holen“, „Andere
  Taste wählen“). Numpad-Tasten liefern nur bei eingeschaltetem NumLock „Num 1“ usw.
  Bewirkt eine globale Taste nichts (Sound lädt noch, Datei fehlt, kein Ziel), während das
  Fenster nicht vorn ist, meldet das Infobereich-Symbol es als stille Windows-Benachrichtigung
  – je Grund höchstens alle 10 Sekunden.
- **Stopp-Taste und „Mikrofon stumm“:** stehen als erste Zeilen im Reiter „Tasten“, gebaut wie
  die Pad-Zeilen. Stopp ist zu Beginn **Pause**, lässt sich ändern oder mit Entf löschen.
  „Mikrofon stumm“ (Standard: keine Taste) schaltet die Stimme im Mix aus und wieder ein; die
  Sounds gehen weiter aufs Kabel. Diese beiden Tasten teilt das Programm mit niemandem.
- **Mithören:** Schalter und Gerät, auf dem du die Sounds selbst hörst (per ID gemerkt).
- **Stop:** stoppt alle laufenden Sounds. Solange nichts spielt, ist er ein ruhiger Knopf mit
  rotem Quadrat; rot wird er, sobald etwas spielt.
- **Regler** (Lautstärke aller Sounds, Mikro-Lautstärke, Pad-Lautstärke im Rechtsklick-Menü):
  Ein Klick auf eine Stelle der Leiste setzt den Wert dort, und ohne loszulassen lässt sich
  gleich weiterziehen. Mit der Tastatur: Pfeiltasten (1 %), Bild auf/ab (10 %).
- **Statuszeile:** Kabel (mit Pegel), Mikrofon, Tasten – ein Klick öffnet die passende
  Einstellungsseite. Ein Warnzeichen erscheint, wenn das Kabel nicht sendet, eine Taste
  nicht wirkt oder das Mikrofon stumm ist („Mikrofon: stumm“ – ein Klick schaltet es wieder
  ein; im Infobereich-Menü gibt es denselben Schalter zum Abhaken). Nach einem Neustart ist
  das Mikrofon immer an. Fehlt das Kabel ganz, steht dort fett „Kabel fehlt“, und der Klick öffnet die
  Einrichtung; ein eigener Hinweis über den Pads erscheint dafür nicht.
  **„Audio“ mit Warnzeichen** erscheint nur, wenn in Windows-Audio etwas nicht mehr geht: ein
  Standardgerät (Wiedergabe oder Aufnahme, Multimedia oder Kommunikation) fehlt, ist deaktiviert
  oder abgezogen; das Kabel ist Standard-Wiedergabegerät (dann hört man nichts) oder neu
  Standardmikrofon geworden; das Standard-Wiedergabegerät ist stumm oder auf 0 %. Der Tooltip
  nennt den Grund, ein Klick öffnet „Audio-Zustand“. Ein anderes, funktionierendes Standardgerät
  (z. B. selbst auf Kopfhörer umgestellt) ist kein Problem und steht nur leise im Reiter.
  Ist das Kabel Standardgerät geworden und das gesicherte Gerät da, stellt das Soundboard es still
  zurück (siehe Einrichtung); höchstens zweimal je Rolle und Lauf – macht etwas das Kabel immer
  wieder zum Standard (ein anderes Programm oder du selbst mit Absicht), hört es auf, dagegen
  anzukämpfen, und es bleibt beim Warnzeichen. Wer das Kabel bewusst als Standard will, sichert den
  Zustand mit „Jetzigen Zustand als normal sichern“; dann bleibt es so.
  Hintergrund: Virtuelle Audio-Software kann Windows-Standardgeräte übernehmen; läuft sie nicht,
  ist kein Ton da. Stumm und Lautstärke melden keine Geräte-Ereignisse; das Soundboard liest sie
  neu, wenn sein Fenster nach vorn kommt.
- **Hinweise:** Es steht immer höchstens ein Hinweis über den Pads – ein Feld wie die
  anderen, erkennbar am bernsteinfarbenen Warnzeichen. Gibt es mehrere,
  blättert „Nächster“ weiter.
- **Fenster schließen** lässt das Programm im Infobereich (Soundboard-Symbol: Taste mit
  Wellenform) weiterlaufen, damit Discord weiter dein Mikrofon bekommt. **Beenden** über Rechtsklick auf das Symbol; die
  Rückfrage im Stil der App ([Beenden] / [Weiterlaufen lassen]) sagt, dass Discord dann Stille
  vom Kabel hört, und nennt Windows-Audio nur, wenn dort etwas nicht geht.
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
- **Absturz:** Ein kleiner Wächter-Prozess merkt, wenn das Programm endet. Er stellt **still und
  ohne Rückfrage** zurück, was das Programm für diesen Lauf an Windows geändert hatte (Ducking; das
  Kabel-Format bleibt, Standardgeräte waren schon die gesicherten). Ein Fenster zeigt er nur, wenn
  das Programm unerwartet endete, während das Kabel lief – dann ist das Mikrofon in Discord jetzt
  stumm –, mit „Soundboard neu starten“. Sonst bleibt er still. Lief auch der Wächter nicht mehr
  (Stromausfall), stellt der nächste Start zurück.

### Einstellungen (Zahnrad)

Die Einstellungen legen sich über die Pads; Kopfzeile und „Stop“ bleiben. Esc, „Schließen“ oder
ein zweiter Klick aufs Zahnrad führt zurück. Das Zahnrad öffnet sie immer auf „Mikrofon & Kabel“; die
Einrichtung beim ersten Start, „Kabel fehlt“, „Audio“ mit Warnzeichen und die Tasten-Warnung öffnen ihren
eigenen Reiter.

| Reiter | Inhalt |
|---|---|
| Mikrofon & Kabel | ans Kabel senden; Mikrofon; Mikro-Lautstärke (wirkt nur im Soundboard, nicht in Windows); Pegel und Testton; Puffer klein/mittel/groß (größer wählen, wenn die Stimme knackt); Stimme stumm, solange ein Sound läuft; „Kabel auf 48 kHz stellen“ (Standard an; darunter, mit welcher Rate das Kabel läuft); „Andere Töne bei Anrufen nicht leiser stellen“ (Windows-Ducking aus, solange das Soundboard läuft; Standard an); 10-Sekunden-Test als WAV im Ordner `Aufnahmen` |
| Einrichtung | die drei Schritte: Kabel, Discord, Rauschunterdrückung (siehe oben) |
| Tasten | Tasten pausieren; „Alle Sounds stoppen“, „Mikrofon stumm“ und alle Pads mit Taste, nur Abweichungen stehen dabei, Ausweg bei Konflikten; Sounds überlagern (aus: ein neuer Sound stoppt den laufenden); „Dieselbe Taste, während der Sound läuft“: Stoppen oder Von vorn spielen |
| Audio-Zustand | oben, nur bei einem Problem, was in Windows-Audio nicht geht; Vergleich „gesichert – jetzt“ für Standardgeräte, Geräte, Lautstärken; „Standardgerät zurückstellen“ (bei abweichendem Standardgerät), „Jetzigen Zustand als normal sichern“ (z. B. nach eigenem Umstellen), „Neu vergleichen“; leise, falls das Kabel eben als Standardgerät zurückgestellt wurde; „Vom Soundboard geändert“ mit Liste und „Alle Änderungen zurücknehmen“; darunter die Audiogeräte in Windows (nur angezeigt, die Endpunkt-ID im Tooltip der Zeile) |
| Darstellung | Sprache (English, Deutsch); hell oder dunkel (wie Windows, hell, dunkel); Akzentfarbe; Pad-Farben (bunt oder alle in der Akzentfarbe); Pad-Größe (groß, kompakt); weiches Aufleuchten an/aus; Profil exportieren/importieren; mit Windows starten (nur im Infobereich) |

**Profil:** „Profil exportieren …“ speichert alle Pads mit ihren Sounds und Bildern als eine
ZIP-Datei (`config.json`, `Sounds\`, `Bilder\`); Geräte-IDs, Gerätenamen und das notierte
Discord-Mikrofon bleiben draußen. „Profil importieren …“ zeigt zuerst, wie viele Pads in der
Datei stecken, dann „Zu den vorhandenen hinzufügen“, „Ersetzen“ oder „Abbrechen“; „Lautstärke
der neuen Pads angleichen“ ist vorgewählt. Vor dem Import sichert das Programm die jetzigen
Pads als ZIP in `Sicherungen\`, „Rückgängig“ holt sie zurück. Aus der Datei werden nur
`config.json`, `Sounds\<Name>` und `Bilder\<Name>` gelesen; Namen mit Ordnern oder „..“
werden verworfen. Belegte Tasten bleiben, wo sie sind – ein importiertes Pad gibt eine schon
vergebene Taste ab.

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
Grau („automatisch“: die Farbe folgt dem Platz, auch nach Verschieben und Entfernen). Rechtsklick
→ „Farbe ▸“ gibt einem Pad eine feste Farbe. Profile bis Version 0.13 werden umgestellt, ohne
dass sich eine Farbe ändert: Pads, deren Farbe nicht zu ihrem Platz passt, gelten als gewählt. Profile aus Version 0.10 werden beim ersten Start umgestellt: Die alten Farben nach Sanzo
Wada wandern auf die nächste neue (Pfauenblau → Petrol, Olympiablau → Blau, Blauviolett →
Violett, Purpur → Pink, Olivgelb und Grün → Grün, Schiefer → Blau bzw. bei Pads Grau); eine
eigene Akzentfarbe und selbst gewählte Pad-Farben bleiben.

**Animationen:** Das Aufleuchten eines Pads blendet kurz ein (160 ms); Schalter gleiten; das Zahnrad dreht
beim Darüberfahren um zwei Zähne vor und beim Verlassen zurück; die Einstellungen gleiten vom Zahnrad auf
und zu; der Strich unter dem offenen Reiter gleitet zum neuen Reiter (200 ms). Ist „Pads leuchten weich auf“ im
Reiter „Darstellung“ aus oder sind in Windows die Animationseffekte ausgeschaltet, springt alles sofort an
seinen Platz. Was per Taste ausgelöst wird, bewegt sich nicht.

Fehlt etwas (kein Mikrofon, Taste belegt), erscheint ein Hinweis mit Warnzeichen und dem nächsten
Schritt – bei Pads direkt auf dem Pad. Ein fehlendes Kabel zeigt nur die Statuszeile (siehe
oben). Knöpfe werden nie ausgegraut.

## Was das Soundboard an Windows ändert

| Einstellung | Wann geändert | Wann zurückgestellt |
|---|---|---|
| Standardgeräte (Wiedergabe/Aufnahme, je Rolle) | nur zurück: wenn Windows das Kabel zum Standard gemacht hat und das gesicherte Gerät da ist (siehe Einrichtung) | – (das ist schon der Zustand von vorher) |
| Kommunikations-Absenkung („Ducking“, `HKCU\Software\Microsoft\Multimedia\Audio`, Wert `UserDuckingPreference`) | beim Start auf „nichts unternehmen“ (3), solange der Schalter „Andere Töne bei Anrufen nicht leiser stellen“ (Reiter „Mikrofon & Kabel“, Standard an) an ist | beim Beenden, beim Abmelden/Herunterfahren, nach einem Absturz durch den Wächter, sonst beim nächsten Start; außerdem beim Ausschalten des Schalters, mit „Alle Änderungen zurücknehmen“, `--restore` und beim Deinstallieren |
| Standardformat des Kabels („CABLE Input“ und „CABLE Output“, nur diese beiden) | beim Öffnen des Kabels auf 48 000 Hz, Kanäle und Bittiefe bleiben; solange der Schalter „Kabel auf 48 kHz stellen“ (Reiter „Mikrofon & Kabel“, Standard an) an ist | mit „Alle Änderungen zurücknehmen“, beim Ausschalten des Schalters, `--restore` und beim Deinstallieren – **nicht** beim normalen Beenden und nicht nach einem Absturz |

**Ducking:** Windows stellt andere Töne um 80 % leiser, sobald es eine „Kommunikation“ erkennt
(Discord zählt). Fehlt der Wert in der Registry (Windows-Standard, 80 %), merkt sich das Journal
genau das und **löscht** ihn beim Zurückstellen wieder, statt 1 zu schreiben. Zurückgestellt wird
nur, solange noch der Wert des Soundboards (3) drinsteht; hat jemand die Einstellung inzwischen in
Windows geändert, bleibt sie so. **Unsicher:** Ob Windows den Registry-Wert sofort übernimmt, ist
nicht belegt. Die Systemsteuerung (Sound → Kommunikation) wirkt sofort, ein reiner
Registry-Eintrag nach Berichten aus Foren eventuell erst für neue Anrufe, nach Neustart des
Windows-Audiodienstes oder nach einer neuen Anmeldung. Am echten Rechner prüfen.

**Kabel auf 48 kHz:** VB-CABLE kommt mit 44,1 kHz; Discord, die meisten Mikrofone und die
Wiedergabe laufen mit 48 kHz, also müsste das Soundboard umrechnen. Es stellt deshalb das
Standardformat beider Kabel-Endpunkte (Wiedergabe „CABLE Input“, Aufnahme „CABLE Output“) über
dieselbe Windows-Schnittstelle wie die Systemsteuerung (`IPolicyConfig::SetDeviceFormat`, Geräte-
und Mix-Format) auf 48 000 Hz, nur die Rate, Kanäle und Bittiefe bleiben. Vor dem Schreiben prüft
es Name und Richtung des Endpunkts so, wie Windows sie gerade meldet: **Echte Geräte werden nie
umformatiert.** Zurückgestellt wird über die gespeicherte Endpunkt-ID und Richtung (IDs sind
eindeutig; so klappt es auch, wenn das Kabel inzwischen umbenannt wurde), und nur, solange Rate, Kanäle
und Bittiefe noch die vom Soundboard gesetzten sind. Danach öffnet es das Kabel neu; läuft es mit 48 kHz, rechnet es nichts mehr um.
Geht es nicht (Windows lehnt ab oder bleibt bei der alten Rate), läuft es still mit Umrechnung
weiter, und unter dem Schalter steht, warum. Das Format bleibt beim Beenden stehen: Das Kabel gehört
nur dem Soundboard, für andere Programme ändert sich nichts, und ein Umstellen bei jedem Start und
Ende würde Discords Mikrofon-Eingang jedes Mal kurz unterbrechen. Zurück geht es, wenn das
Soundboard entfernt wird (oder mit „Alle Änderungen zurücknehmen“). Beim ersten Umstellen
verliert Discord, falls es das Kabel gerade hört, kurz den Eingang. **Ungeprüft:** das Schreiben
selbst (am Entwicklungsrechner war kein Kabel installiert; das Lesen derselben Schnittstelle ist an
einem echten Gerät geprüft).

**Das Journal** (`changes.json` im Datenordner) hält je Änderung den alten Wert, die Art
(„solange es läuft“ oder „bis zum Deinstallieren“) und den Prozess, der sie gemacht hat. Ablauf
jeder Änderung: Eintrag schreiben → Windows ändern → Eintrag als erledigt markieren;
Zurückstellen entfernt ihn. Das Zurückstellen ist idempotent (zweimal ist wie einmal), und ein
Eintrag, dessen Rückstellung scheitert, bleibt für den nächsten Versuch stehen. App, Wächter,
`--restore` und Rückbau greifen über eine gemeinsame Sperre zu; ist sie nach 10 Sekunden nicht frei,
wird nichts geändert und der Eintrag bleibt. Geschrieben wird mit Durchschreiben auf die Platte, dann
umbenannt, damit nach einem Stromausfall das alte oder das neue Journal da ist, nicht ein leeres. Ein
trotzdem unlesbares Journal wird als `changes.defekt-<Zeit>.json` beiseitegelegt, nie überschrieben
und vom Rückbau nicht gelöscht; seine alten Werte stellt das Soundboard dann aber nicht mehr von selbst
zurück (von Hand über Sound → Kommunikation bzw. die Kabel-Eigenschaften). `--restore` und der
Rückbau schalten außerdem die beiden Schalter in den Einstellungen aus, damit ein späterer Start (etwa
per Autostart) nichts wieder umstellt. Der Wächter erkennt seinen Prozess an ID und Startzeit, damit
eine wiederverwendete Prozess-ID nicht die Änderungen eines neuen Laufs zurückstellt. Im Reiter „Audio-Zustand“ zeigt
**„Vom Soundboard geändert“** die Liste („Andere Töne bei Anrufen: volle Lautstärke, vorher um
80 % leiser“) mit **„Alle Änderungen zurücknehmen“**: stellt alles sofort zurück und schaltet
die Schalter aus, die es gesetzt haben.

**Restrisiko:** Stromausfall oder hartes Ausschalten, und danach wird das Soundboard nie wieder
gestartet: Dann bleibt das Ducking auf „nichts unternehmen“, bis `Soundboard.exe --restore`
läuft oder das Soundboard deinstalliert wird (der Deinstaller ruft das auf). Bricht ein anderes Programm das
Abmelden ab, ist das Ducking schon zurückgestellt, das Soundboard läuft aber weiter; der Schalter
wirkt dann erst wieder nach Aus- und Einschalten oder beim nächsten Start. Ist ein Eintrag geschrieben,
aber Windows noch nicht geändert, und stellt jemand danach selbst „nichts unternehmen“ ein, schreibt
das Zurückstellen den alten Wert über diese Wahl (selten, hingenommen).

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

`--restore` stellt zuerst alles aus dem Änderungs-Journal zurück (siehe oben), dann die
Standardgeräte – diese nur auf IDs aus der Sicherung, nur wenn das
Gerät angeschlossen und aktiv ist, und nie ein virtuelles Kabel. Was nicht geht, wird mit
dem Weg „von Hand“ ausgegeben.

## Rückbau

Rechtsklick auf das Soundboard-Symbol im Infobereich → **„Alles entfernen …“** oder `Soundboard.exe --uninstall`:

1. **Änderungen zurückstellen und Audio-Zustand prüfen:** Alles, was das Soundboard an Windows
   geändert hat (Journal), geht beim Öffnen der Anleitung sofort zurück; das Ergebnis steht dort.
   Bei abweichendem Standardgerät „Standardgerät zurückstellen“.
2. **Eigene Daten löschen** – erst nach Rückfrage; „Sounds behalten“ ist vorausgewählt und
   behält auch die Pad-Bilder. Entfernt werden Einstellungen, Sicherungen, Test-Aufnahmen und
   die Sicherungen vor Importen und der Autostart-Eintrag, falls er diese EXE startet (einer für
   eine andere Kopie bleibt); ohne Häkchen auch Sound- und Bilder-Ordner. Datenordner und
   `pnputil`-Befehle stehen im Fenster auf eigener Zeile zum Kopieren.
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
| `Sicherungen\` | ZIP der Pads vor jedem Import (`vor-import-<Datum>.zip`) |
| `audio-snapshot.json` | gesicherter Windows-Audio-Zustand (automatisch: beim ersten Start und wenn Geräte dazukommen oder wegfallen, solange die Standardgeräte wie gesichert sind und alles geht) |
| `audio-snapshot.vorher.json` | die Sicherung davor |
| `running.json` | Merker „läuft gerade“; bleibt er nach dem Ende liegen, war es ein Absturz |
| `changes.json` (ggf. `changes.defekt-*.json`) | Änderungs-Journal: was das Soundboard an Windows geändert hat, mit dem alten Wert (beim Kabel-Format das alte Geräte- und Mix-Format als Bytes); existiert nur, solange etwas geändert ist. Wird beim Rückbau nicht gelöscht, solange etwas darin steht |

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
