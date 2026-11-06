# Soundboard für Windows 11 — Konzept

Stand: 05.11.2026 · Autorin: Malenya · Status: Konzept, kein Code · Fassung 2 (Kabel-Weg)

## 0. Kurzfassung

Drei Anforderungen: (1) Sounds müssen in Discord ankommen, zusammen mit dem eigenen Mikrofon;
(2) die Windows-Soundeinstellungen dürfen nicht leiden — nach Schließen und Deinstallieren ist
alles wie vorher; (3) einfache Bedienung.

Entscheidung des Nutzers: **Kabel-Weg.** Die App nimmt das echte Mikrofon auf, mischt die
Sounds dazu und gibt die Summe auf ein **virtuelles Audiokabel**. Nur **Discord** wählt in
seinen eigenen Spracheinstellungen das Kabel als Mikrofon. **Windows-Standardgeräte werden
nie auf etwas Neues gesetzt.** Das Kabel ist ein Fremdtreiber (VB-CABLE), den der Nutzer selbst installiert;
die App sichert vorher den Audio-Zustand und meldet, wenn der Installer etwas verstellt hat.

---

## 1. Technische Wege — Bewertung und Entscheidung

Discord nimmt pro Client genau **ein** Eingabegerät entgegen und schickt es durch
Echounterdrückung, Rauschunterdrückung (Krisp) und automatische Verstärkung. Alles, was „durchs
Mikro" soll, muss in dieses eine Gerät gemischt werden.

### Gewählt: virtuelles Kabel + Mikrofon-Mix in der App

```
Mikrofon (WASAPI Capture, shared) ─┐
                                   ├─ Mixer ─► CABLE Input (WASAPI Render, shared) ─► Discord liest „CABLE Output"
Sounds (Decoder, Lautstärke) ──────┤
                                   └─ nur Sounds ─► Kopfhörer („Mithören")
```

Warum das die Windows-Einstellungen schont: Die Wahl des Eingabegeräts speichert **Discord
selbst**, nicht Windows. Es muss kein Standard- oder Kommunikationsgerät umgestellt werden.
Genau das ist der Unterschied zu Voicemeeter, Voicemod & Co. (Abschnitt 8), die sich als
Windows-Standard eintragen, damit *alle* Programme sie sehen — und es dann nicht zurückstellen.

Eigenschaften:

- **Treiber/Admin:** ja, einmalig für das Kabel (Kernel-Treiber, Adminrechte, Neustart
  empfohlen). Die App selbst braucht keine Adminrechte.
- **Was ändert sich am System:** zwei neue Audio-Endpunkte („CABLE Input" als Wiedergabe,
  „CABLE Output" als Aufnahme). Sonst nichts — mit einer Ausnahme: Das VB-CABLE-Handbuch
  warnt, dass Windows 10/11 den **zuletzt installierten Audiotreiber automatisch zum
  Standardgerät machen kann** ([VBCABLE ReferenceManual](https://vb-audio.com/Cable/VBCABLE_ReferenceManual.pdf)).
  Dagegen steht das Rücksetz-Konzept (Abschnitt 2).
- **Wie sauber geht es weg:** Der VB-CABLE-Uninstaller lässt laut Handbuch gelegentlich Reste,
  Nachputzen im Geräte-Manager; Foren berichten von verwaisten Einträgen und Installer-Fehlern
  ([VB-Audio-Forum](https://forum.vb-audio.com/viewtopic.php?p=6905),
  [24H2-Thread](https://forum.vb-audio.com/viewtopic.php?p=7505)). Das ist das Restrisiko
  dieses Wegs und liegt außerhalb unserer App — die App leitet die Nachkontrolle an.
- **Latenz:** Mikro → App → Kabel → Discord. Shared Mode mit 10-ms-Periode: etwa 20–40 ms
  zusätzlich zum normalen Sprachweg. Für Sprache unhörbar. Mithören der Sounds auf dem
  Kopfhörer: ~10–20 ms.
- **Klangqualität:** Discords Rauschunterdrückung (Krisp) behandelt Soundboard-Clips als
  Störgeräusch und dämpft sie; Echounterdrückung und AGC tun ihr Übriges. Übliche Abhilfe:
  Rauschunterdrückung „Keine" (oder „Standard"), Echounterdrückung aus, automatische
  Verstärkung aus — alles in Discord, nicht in Windows
  ([simpleclean.app](https://simpleclean.app/blog/discord-noise-suppression-not-working),
  [VoxBooster, Anbieterquelle](https://voxbooster.com/blog/voice-changer-voicemod-soundboard-doesnt-work-on-discord/)).
  Die App kann das nur erklären, nicht umstellen (siehe 4.2).
- **Push-to-Talk:** Discord behandelt das Kabel wie ein Mikrofon. Mit PTT hört man Sounds nur,
  solange die PTT-Taste gehalten wird. Für Soundboards empfiehlt sich Sprachaktivierung oder
  eine zweite PTT-Taste. Tastendrücke in Discord zu simulieren (SendInput) wäre
  Client-Automatisierung — Grauzone, machen wir nicht.
- **Schwachstelle:** Stürzt die App ab oder wird beendet, bekommt Discord vom Kabel **Stille**
  — das eigene Mikro ist in Discord stumm, bis man dort zurückschaltet oder die App neu
  startet. Abhilfe: Wächterprozess mit Hinweis, Tray-Symbol mit Status, und die App läuft
  im Hintergrund weiter, wenn das Fenster geschlossen wird (Beenden nur über Tray).
- **Anticheat/ToS:** Signierte virtuelle Audiotreiber sind weit verbreitet; einen offiziellen
  Bann durch Vanguard/FACEIT für VB-CABLE habe ich nicht gefunden (nur Anbieter-Behauptungen,
  unsicher). Testsigning-Modus ist dagegen sicher ein Ausschluss
  ([Riot Support](https://support.riotgames.com/en-us/riot/security/restore-windows-security-to-normal-state)).
  Discord-ToS: unproblematisch — Discord sieht ein Mikrofon, mehr nicht.

### Welches Kabel?

| Treiber | Signiert für Win 11? | Lizenz | Bewertung |
|---|---|---|---|
| **VB-CABLE** (VB-Audio, Pack 45, Okt. 2024, XP–Win 11, auch Arm64) | installiert auf normalen Win-11-Rechnern ohne Testsigning, also produktiv signiert; eine ausdrückliche Aussage von VB-Audio zur Signierung habe ich nicht gefunden (**unsicher**) | Donationware, siehe unten | **gewählt** |
| VirtualDrivers/Virtual-Audio-Driver (GitHub, MIT + MS-PL, aus Microsofts SysVAD-Sample) | **nein** — Beta, verlangt Testsigning ([README](https://github.com/VirtualDrivers/Virtual-Audio-Driver)) | MIT | für Endnutzer ungeeignet (Anticheat, Secure-Boot-Fehler Code 52) |
| HSpear/virtual-audio-wire (GitHub) | nein, nur MSVAD-Sample-Stand, Treiber-Know-how nötig ([Repo](https://github.com/HSpear/virtual-audio-wire)) | Open Source | nein |
| Eigener SysVAD-Abkömmling | nur mit Microsoft-Attestation-Signierung (Hardware Dev Center, EV-Zertifikat, Partnerkonto) ([OSR-Forum](https://community.osr.com/discussion/comment/297791), [MS Q&A](https://learn.microsoft.com/en-in/answers/questions/1668206/i-fail-to-sign-a-windows-audio-driver-for-a-virtua)) | — | für ein Freizeit-Repo unrealistisch |
| Thesycon TVirtAudio | ja (attestiert) | kommerziell, nicht für Endnutzer ([Thesycon](https://thesycon.de/eng/virtual_audiodriver.shtml)) | nein |
| Virtual Audio Cable (Muzychenko) | ja | kommerziell, Testversion mit Störton | nein |

Ergebnis: **Es gibt keine freie, signierte Open-Source-Alternative** (Stand der Recherche;
AlternativeTo-Listen führen nur Router wie JACK/Carla, keine signierten Treiber
([AlternativeTo](https://alternativeto.net/software/vb-audio-cable/?license=opensource))).
VB-CABLE ist die einzige kostenlose, signierte, gepflegte Wahl.

### VB-CABLE: Lizenz und Weitergabe

Quelle: [VB-Audio Licensing](https://www.vb-audio.com/Services/licensing.htm).

- VB-CABLE ist **Donationware**: frei nutzbar, Zahlung freiwillig bzw. bei professionellem
  Einsatz erwartet.
- **Mitliefern ist erlaubt:** „We allow the distribution of the VB-CABLE package AS IS
  together with your application" — freeware oder kommerziell; auch stille Installation aus
  dem eigenen Installer heraus, **sofern** das Donationware-Modell gewahrt bleibt: Der
  Endnutzer muss VB-CABLE als VB-Audio-Produkt erkennen und spenden/zahlen können.
  Dokumentation, Installer oder App müssen die Herkunft (vb-cable.com) und „Donationware"
  nennen; Vorschlagstext: „VB-CABLE is a donationware, all participations are welcome."
- **Nicht erlaubt:** VB-CABLE A+B / C+D mitliefern (nur das Standard-Kabel).
- **Firmen/Behörden:** Wer es so ausrollt, dass Endnutzer nicht spenden können, muss
  Lizenzen kaufen (Staffel ab 100 Stück, ~3,61 €).
- VB-Audio nennt den Verweis auf die eigene Downloadseite ausdrücklich den einfachsten Weg.

**Entscheidung für uns:** **Nur verlinken, nicht mitliefern.** Gründe: Das Repo ist MIT und
soll frei von Fremd-Binaries bleiben; die Download-Pakete ändern sich (Pack 45 löste 24H2-
Probleme); die Verantwortung für Treiber-Installation und -Entfernung bleibt klar bei
VB-Audios eigenem Setup. Die App zeigt den Link, den Donationware-Hinweis und prüft nach der
Installation, ob das Kabel da ist. Unsicher: ob eine spätere Installer-Fassung das Paket
mitliefern darf, ohne Lizenzgebühr — nach dem Lizenztext ja, aber vor einem Mitliefern
bei VB-Audio nachfragen.

### Verworfen

- **Discord-Bot im Sprachkanal:** Entscheidung des Nutzers gegen den Bot; außerdem nur auf
  Servern mit Einladung, nicht in DMs/Gruppenanrufen, und seit 01.03.2026 DAVE-Pflicht
  ([Discord Dev-Doku](https://docs.discord.com/developers/topics/voice-connections)).
- **Discords eigenes Soundboard:** sauber, aber 5,2 s, 512 KB, Slots je Server
  ([Soundboard Guide](https://support.discord.com/hc/en-us/articles/12612888127767-Discord-Soundboard-Guide-Using-Adding-and-Managing-Sounds),
  [Developer-Doku](https://discord.com/developers/docs/resources/soundboard)) — keine
  Hotkeys, keine Lautstärke je Sound. Bleibt als Ergänzung für kurze Clips.
- **Self-Bot:** Automatisierung des Nutzerkontos → Kontosperre
  ([Discord: Self-Bots](https://support.discord.com/hc/en-us/articles/115002192352-Automated-User-Accounts-Self-Bots)).
- **Eigener Treiber / Soundpad-Art (direkt ins Mikro-Signal):** Signierhürde (s. Tabelle).
- **WASAPI-Loopback allein:** bringt nichts in Discord hinein, braucht trotzdem ein Kabel.
  Nur Baustein (z. B. später Mitschneiden).
- **Discord RPC/Overlay:** kann Status und Anzeige, kein Audio.

---

## 2. Rücksetz-Konzept

### 2.1 Grundsatz: Ändern erlaubt, Zurückstellen Pflicht

Gelockert am 05.11.2026. Vorher galt „nichts Globales ändern“. Jetzt gilt: **Die App darf
Windows-Einstellungen ändern, wenn es die Bedienung einfacher macht — aber sie muss sie zuverlässig
zurückstellen**, beim normalen Beenden, beim Deinstallieren und nach einem Absturz (Wächter), sodass
Windows ohne die App wieder normal klingt. Der Maßstab bleibt der Fehler anderer Audio-Software, die
Standardgeräte übernimmt und ohne die dann kein Ton mehr da ist.

- Jede Änderung läuft über das **Änderungs-Journal** (2.6): alter Wert zuerst auf die Platte,
  dann Windows ändern; Zurückstellen entfernt den Eintrag.
- Standardgeräte stellt die App per `IPolicyConfig::SetDefaultEndpoint` nur **zurück** auf die
  gesicherten Geräte, nie auf etwas Neues und nie auf das Kabel (2.3).
- Geräte werden ausschließlich **per Endpunkt-ID** geöffnet (`IMMDeviceEnumerator`), immer
  im **Shared Mode**, nie exklusiv, im Mix-Format des Geräts (Resampling in der App). Umformatiert
  wird nur das Kabel (2.6), nie ein echtes Gerät.
- Kein Dienst, keine Aufgabenplanung, kein Autostart ohne ausdrückliches Häkchen.
- Keine Änderung an „Gerät abhören", Exklusivmodus-Erlaubnis, Verbesserungen (APOs) oder
  Lautstärken fremder Endpunkte. Die eigene Mix-Lautstärke regelt die App intern, nicht
  über die Windows-Lautstärke des Kabels.
- Portable Betrieb (ein Ordner, Konfiguration daneben oder in `%LOCALAPPDATA%`) ist
  Standard.

Was die App ändert (Stand 0.15): die Kommunikations-Absenkung („Ducking“) solange sie läuft, und
das Standardformat des Kabels (48 kHz) bis zum Entfernen (2.6).
Dazu kommt die Treiberinstallation — ausgeführt vom Nutzer mit VB-Audios Setup, beaufsichtigt von
unserer App.

### 2.2 Snapshot: was gesichert wird

Die App schreibt `audio-snapshot.json` mit Zeitstempel und App-Version **selbst im
Hintergrund**: beim ersten Start (also vor dem Treiber-Download) und immer dann, wenn Geräte
dazukommen oder wegfallen (z. B. das Kabel nach dem Setup), solange die Standardgeräte noch die
gesicherten sind und nichts kaputt ist. Ein Standardgerät, das jemand anders umgestellt hat,
übernimmt sie nie still; das geht nur über den Nebenknopf „Jetzigen Zustand als normal sichern"
im Reiter „Audio-Zustand“. Gesichert wird:

1. Standard-Endpunkte für Render und Capture × Rollen eConsole, eMultimedia,
   eCommunications: ID + Anzeigename (`GetDefaultAudioEndpoint`).
2. Je aktivem Endpunkt: Master-Lautstärke, Stummschaltung (`IAudioEndpointVolume`),
   Zustand (aktiv/deaktiviert/nicht angeschlossen).
3. Je Aufnahmegerät: „Dieses Gerät als Wiedergabequelle verwenden" + Zielgerät. Liegt im
   Property Store des Endpunkts; den genauen PKEY habe ich nicht belegt (**unsicher** — in
   der Umsetzung an SoundVolumeView/SoundSwitch-Quellen prüfen).
4. „Exklusivmodus erlauben" und Standardformat je Endpunkt (Property Store, Schlüssel
   **unsicher**). Nur zum Erkennen fremder Änderungen; geschrieben wird nur das Format des Kabels
   (2.6).
5. Liste aller Endpunkte mit IDs — um nach der Treiberinstallation zu sehen, was neu ist.

Zum Zurückschreiben der Standardgeräte ist `IPolicyConfig` der einzige Weg (undokumentiert,
seit Vista stabil, in SoundSwitch/AudioSwitch im Einsatz —
[SoundSwitch-Interop](https://github.com/Belphemur/SoundSwitch/blob/bbc1cb9cc5a70cd52aca2bc751218759015065e1/SoundSwitch.Audio.Manager/Interop/Client/ExtendedPolicyClient.cs),
[windows-rs #1355](https://github.com/microsoft/windows-rs/issues/1355)). Nur fürs
*Zurück*, nie fürs *Hin*. Schlägt der Aufruf fehl (Microsoft ändert die Schnittstelle),
zeigt die App eine Anleitung „so stellst du es von Hand zurück" mit den gesicherten Namen.

### 2.3 Wann geprüft und zurückgestellt wird

Gemeldet wird nicht jede Abweichung, sondern nur, **was nicht mehr geht** — klein, als „Audio“
mit Warnzeichen in der Statuszeile (Tooltip nennt den Grund, Klick öffnet „Audio-Zustand“), nie
als Banner. Grund für die ganze Vorsicht: Virtuelle Audio-Software kann Windows-Standardgeräte
übernehmen; läuft sie nicht, ist gar kein Ton da. „Geht nicht mehr“ heißt, für die Rollen
Multimedia und Kommunikation:

- kein Standardgerät, obwohl eines gesichert war (Gerät fehlt, deaktiviert, abgezogen);
- das Standardgerät selbst ist nicht aktiv;
- das Kabel ist Standard-Wiedergabegerät (man hört nichts) oder neu Standardmikrofon geworden;
- das Standard-Wiedergabegerät ist stumm oder auf 0 %.

Ein anderes, funktionierendes Standardgerät (selbst auf Kopfhörer umgestellt, oder Windows ist
nach dem Abziehen auf die Lautsprecher gewechselt) ist **kein** Problem und steht nur im Reiter.
Im Zweifel kein Warnzeichen. Nicht erkennbar ist ein virtuelles Gerät, das aktiv gemeldet wird,
dessen Software aber nicht läuft. Stumm/Lautstärke lösen kein Geräte-Ereignis aus; die App liest
sie zusätzlich, wenn ihr Fenster nach vorn kommt.

| Situation | Mechanismus |
|---|---|
| Nach Treiberinstallation | Die App vergleicht Snapshot und Ist. Neu: CABLE Input/Output → gut, die Sicherung wird still nachgezogen. Kabel als Standard-Wiedergabe oder -Mikro (für eine Rolle, deren gesichertes Gerät kein Kabel war) → die App stellt **sofort still** per `SetDefaultEndpoint` das gesicherte Gerät zurück, wenn es da und aktiv ist, und vermerkt es leise im Reiter (stille Windows-Benachrichtigung, wenn das Fenster hinten ist). Höchstens zweimal je Rolle und Lauf, damit sie nicht gegen ein anderes Programm oder eine bewusste Wahl ankämpft. Ist das gesicherte Gerät nicht da → kein Umstellen, Warnzeichen „Audio“; im Reiter „Standardgerät zurückstellen". |
| Normales Schließen | Streams schließen, Änderungen „solange es läuft“ aus dem Journal zurückstellen (2.6), dann Dirty-Flag löschen. Die Rückfrage nennt Windows-Audio nur, wenn dort etwas nicht geht. |
| Absturz | **Wächter**: die App startet sich selbst als `app.exe --watch <pid>`; der Kindprozess wartet auf das Elternende und stellt dann **still** die Änderungen „solange es läuft“ dieses Prozesses aus dem Journal zurück (2.6) — nach einem Absturz wie nach normalem Ende, wo nichts mehr übrig ist. Ein kleines Fenster zeigt er nur, wenn die App unerwartet endete, während das Kabel lief („Discord hört dein Kabel, aber niemand füttert es“, Knopf „Soundboard neu starten“). Sonst still. Lief auch der Wächter nicht (Stromausfall), stellt der nächste Start die Einträge toter Prozesse zurück. Beim nächsten Start kein Banner; ein Problem zeigt das Warnzeichen. Kein Dienst, keine Adminrechte. |
| Neustart / Windows-Update | Kein erzwungener Autostart. Stattdessen `app.exe --check` und Menüpunkt „Audio-Zustand prüfen". Windows-Updates setzen Geräte gelegentlich um ([Hinweis](https://voicechanger.easeus.com/resources/vb-audio-virtual-cable-not-working.html)) — erkennen und melden. Optional (Häkchen): mit Windows starten, dann greift die Prüfung automatisch. |
| Deinstallation | siehe 2.5 |

### 2.4 Discord hatte schon ein Mikro zugeordnet

Discord speichert die Mikrofonwahl selbst, unabhängig von Windows.

- Der Nutzer stellt in Discord einmal auf „CABLE Output (VB-Audio Virtual Cable)" um. Wir
  schreiben **nicht** in Discords Konfigurationsdateien
  ([Platform-Manipulation-Policy](https://discord.com/safety/platform-manipulation-policy-explainer)
  untersagt Client-Änderungen; eine Einstellungsdatei zu editieren ist vermutlich harmlos,
  aber ohne Not riskieren wir es nicht).
- Welches Mikro Discord vorher hatte, kann die App nicht lesen. Der Assistent bittet den
  Nutzer, den Namen zu notieren, und merkt sich den Text für die Rück-Anleitung.
- **Nach Entfernen des Kabels:** Zeigt Discord noch auf das verschwundene Gerät, fällt es auf
  „Default" zurück = Windows-Standardmikro, das wir nie verändert haben. Discord funktioniert
  wieder wie vor der Installation, ohne unser Zutun. Hatte der Nutzer vorher ein bestimmtes
  (nicht Standard-)Mikro gewählt, zeigt die App den notierten Namen: „In Discord wieder X
  auswählen".

### 2.5 Deinstaller-Schritte

Portable Fassung: App über Tray beenden → Ordner löschen. Zusätzlich, geführt über
„Alles entfernen" im Menü (oder `app.exe --uninstall`):

1. Änderungs-Journal sofort abarbeiten (alles zurück, 2.6), Snapshot prüfen, Standardgeräte
   zurückstellen anbieten.
2. Eigene Spuren: Konfiguration und Snapshot (Rückfrage: Sounds behalten?), Autostart-Eintrag
   falls gesetzt, Tray-Icon-Cache ist Windows-Sache.
3. **Treiber:** nicht still deinstallieren. Die App zeigt den Weg: VB-CABLE-Setup als
   Admin starten → „Remove Driver" → Neustart → Geräte-Manager, „Ausgeblendete Geräte
   anzeigen", verbliebene VB-Audio-Einträge mit „Treibersoftware löschen" entfernen →
   zweiter Neustart. (Erfahrungen aus den VB-Audio-Foren, s. o.) Treiber-Store nur auf
   Wunsch per `pnputil /enum-drivers` / `/delete-driver`.
4. Letzter Bildschirm: Tabelle Snapshot vs. Ist. Grün = identisch. Hinweis zu Discord
   (2.4).

Installer-Fassung (entschieden 03.11.2026, kommt zur fertigen Fassung): Inno Setup, Installation nur
für den Benutzer (`%LOCALAPPDATA%`, ohne Admin), Startmenü, Autostart optional; der Deinstaller ruft
`--uninstall` bzw. `--restore` auf. Die portable EXE wird weiter als zweiter Download angeboten.

### 2.6 Änderungs-Journal

`changes.json` im Datenordner. Je Änderung ein Eintrag: Schlüssel (`ducking`,
`cable-format:<Endpunkt-ID>`), Art, Geltung, Besitzer (Prozess-ID + Startzeit, damit eine
wiederverwendete ID nicht verwechselt wird), Zeit, „erledigt“-Merker und der alte Wert.

- **Write-ahead:** Eintrag mit altem Wert schreiben (atomar: Zwischendatei, dann Umbenennen) →
  Windows ändern → Eintrag als erledigt markieren. Stirbt der Prozess dazwischen, findet das
  Zurückstellen den alten Wert noch in Windows und entfernt nur den Eintrag.
- **Vorsichtig zurück:** Geschrieben wird der alte Wert nur, solange in Windows noch der Wert der App
  steht. Hat jemand die Einstellung inzwischen geändert, bleibt sie so („so gelassen“). Damit ist
  das Zurückstellen idempotent.
- **Nie den eigenen Wert als „vorher“:** Findet ein neuer Lauf einen Eintrag eines alten Laufs und
  in Windows noch den Wert der App, übernimmt er den Eintrag mit dem alten Wert.
- **Fehlender Wert:** Gab es den Registry-Wert vorher nicht, wird er beim Zurückstellen gelöscht,
  nicht auf den Windows-Standard gesetzt.
- **Geltung „solange es läuft“** (Ducking): zurück beim Beenden, beim Abmelden/Herunterfahren
  (`SessionEnding`), durch den Wächter nach Absturz, beim nächsten Start für Einträge toter
  Prozesse (Stromausfall), beim Ausschalten des Schalters.
- **Alle Einträge** gehen zurück mit „Alle Änderungen zurücknehmen“ (schaltet auch die Schalter
  aus), `--restore` und `--uninstall`.
- Scheitert eine Rückstellung, bleibt der Eintrag stehen und wird beim nächsten Anlass wieder
  versucht. Ein unlesbares Journal wird beiseitegelegt (`changes.defekt-<Zeit>.json`), nie
  überschrieben; der Rückbau löscht `changes.json` nicht, solange etwas darin steht.
- Zugriff von App, Wächter, `--restore` und Rückbau unter einer benannten Sperre (Mutex); ohne Sperre
  (Zeitüberschreitung 10 s) wird nichts geändert. Schreiben mit Durchschreiben auf die Platte.
- Der Wächter erkennt seinen Prozess an ID **und** Startzeit (`--watch <pid> <ticks>`).
- `--restore` und `--uninstall` schalten zusätzlich die Schalter im Profil aus, damit ein späterer
  Start nichts wieder umstellt.
- Die automatische Standardgeräte-Rückstellung (2.3) greift nur bei genau „CABLE Input“ (Wiedergabe)
  bzw. „CABLE Output“ (Aufnahme), nicht bei Voicemeeter oder anderen VB-Audio-Geräten.

**Ducking** (`HKCU\Software\Microsoft\Multimedia\Audio`, DWORD `UserDuckingPreference`: 0 alle
anderen stumm, 1 um 80 % (Standard, auch wenn der Wert fehlt), 2 um 50 %, 3 nichts unternehmen):
Beim Start auf 3, solange der Schalter „Andere Töne bei Anrufen nicht leiser stellen“ an ist
(Standard an). Ein Wert anderen Typs wird nicht angefasst. **Unsicher:** ob Windows den reinen
Registry-Eintrag sofort übernimmt — die Systemsteuerung wirkt sofort, Forenberichte nennen für den
Registry-Weg einen Neustart des Audiodienstes oder eine neue Anmeldung
([Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/286017/audio-ducking-stream-attenuation-setting-doesnt-ta)).
Am echten Rechner prüfen; falls nötig, dokumentieren „wirkt für neue Anrufe/nach Neuanmeldung“.

**Kabel-Format** (Geltung „bis zum Entfernen“): Standardformat von „CABLE Input“ (Wiedergabe) und
„CABLE Output“ (Aufnahme) auf 48 000 Hz, wenn es abweicht (VB-CABLE ab Werk 44,1 kHz). Weg:
`IPolicyConfig::GetDeviceFormat`/`GetMixFormat` lesen, nur Rate und Bytes/s ändern, mit
`SetDeviceFormat(id, Geräteformat, Mixformat)` schreiben — dieselbe Schnittstelle wie die
Systemsteuerung, der reine Property-Store-Weg (`PKEY_AudioEngine_DeviceFormat`
{f19f064d-082c-4e27-bc73-6882a1bb8e4c},0) würde das Mix-Format nicht mitziehen. Das alte Format
steht als Bytes im Journal. Vor dem Umstellen müssen Name und Richtung, wie Windows sie gerade meldet, „CABLE Input“/Wiedergabe
bzw. „CABLE Output“/Aufnahme sein — nie ein echtes Gerät. Zurück geht es über die gespeicherte ID und
Richtung (auch nach Umbenennen), nur solange Rate, Kanäle und Bittiefe noch die gesetzten sind. Danach wird das Kabel neu geöffnet; bei 48 kHz entfällt das Resampling. Fehlschlag →
still weiter mit Resampling, Vermerk unter dem Schalter. **Nicht beim normalen Beenden zurück:**
Das Kabel gehört nur dem Soundboard, sein Format wirkt auf kein anderes Programm, und jedes
Umstellen setzt beide Endpunkte neu auf — Discords Eingang würde bei jedem Start und Ende kurz
abreißen. Zurück mit „Alle Änderungen zurücknehmen“, Ausschalten des Schalters, `--restore` und
`--uninstall`. Ist das Kabel bis dahin schon entfernt, entfällt der Eintrag; ist es deaktiviert,
bleibt er stehen.

---

## 3. Empfehlung und Reihenfolge

**v1 (MVP):** Kabel-Modus komplett — Kacheln, Hotkeys, Mithören, Mikro-Durchreichen auf das
Kabel, Snapshot/Prüfung, Assistent, Wächter, portable EXE. Erst lokal ohne Discord
testbar (Senke „Datei"/„Kopfhörer"), dann Kabel, dann Discord.

**Später (v2):** Autostart-Option, Ordner/Seiten für Kacheln, Clip-Trimmen, Mitschneiden
(Loopback), Streamdeck/MIDI.

**Zur fertigen Fassung:** Installer (Inno Setup, pro Benutzer) als Hülle um dieselbe EXE, Eintrag
in „Apps", Deinstallieren stellt automatisch zurück; portable EXE bleibt zweiter Download.

**Nie:** eigener Kernel-Treiber, Setzen von Windows-Standardgeräten, Schreiben in
Discord-Dateien, Tastendrücke in andere Programme einspeisen, Self-Bot.

---

## 4. Bedienung

### 4.1 Erster Start (Assistent, drei Schritte)

1. **Kabel.** Die App sucht einen Endpunkt mit Namen „CABLE Input"/„CABLE Output". Fehlt er:
   gelber Kasten „Es ist kein virtuelles Kabel installiert" mit Link zur VB-CABLE-Seite, dem
   Donationware-Hinweis, der Kurzanleitung (ZIP entpacken, `VBCABLE_Setup_x64.exe` als
   Administrator, Install, Neustart) und dem Satz, dass die App den Audio-Zustand schon selbst
   gesichert hat (Snapshot vor der Installation, siehe 2.2). Nach dem Neustart erkennt die App
   das Kabel und vergleicht.
2. **Discord.** Anleitung: Discord → Einstellungen → Sprache & Video → Eingabegerät =
   „CABLE Output (VB-Audio Virtual Cable)"; Ausgabegerät **unverändert lassen**. Feld
   „Welches Mikro war vorher gewählt?" (Freitext, für die Rück-Anleitung). Pegelanzeige
   „Das sendet dein Kabel gerade" — spricht der Nutzer, schlägt sie aus; Testton-Knopf.
3. **Rauschunterdrückung.** Anleitung: in Discord Rauschunterdrückung auf „Keine" (oder
   „Standard"), Echounterdrückung aus, automatische Verstärkung aus; Hinweis, dass
   Sprachaktivierung oder eine zweite PTT-Taste für Soundboards angenehmer ist. Die App
   kann das nicht prüfen; der Nutzer bestätigt mit Häkchen.

Alle drei Schritte sind jederzeit über „Einrichtung" erreichbar. Knöpfe werden **nie**
grau — fehlt etwas, erscheint die gelbe Meldung mit dem fehlenden Schritt.

### 4.2 Hauptfenster (v1)

- **Kachelraster.** Kachel: Name, Farbe, Hotkey, Fortschrittsbalken beim Abspielen. Klick =
  abspielen, Klick während Abspielen = stoppen, Rechtsklick = Einstellungen (Name, Farbe,
  Lautstärke, Hotkey, Datei).
- **Drag & Drop** von MP3/WAV/OGG/FLAC auf das Fenster → neue Kacheln. Dateien werden in den
  Sound-Ordner kopiert, Original bleibt. Mehrere auf einmal.
- **Lautstärke pro Sound** + Hauptlautstärke für Sounds; Mikro-Pegel separat (Standard 100 %,
  nicht anfassen müssen).
- **Mithören** ja/nein in der Kopfzeile, mit Wahl des Kopfhörers (per ID, Voreinstellung:
  aktuelles Standard-Wiedergabegerät, aber nur *gelesen*).
- **Stopp-alles:** großer Knopf + fester Hotkey (Vorschlag: Pause-Taste).
- **Hotkeys global** per `RegisterHotKey`. Vorschlag: Numpad-Tasten; Alternativen
  Ctrl+Alt+Zahl, F13–F24 oder Makrotasten. `RegisterHotKey` fängt die Taste ab, das Spiel
  sieht sie nicht — gewollt. Schalter „Hotkeys pausieren". Belegungskonflikte (Taste schon
  von anderem Programm registriert) werden gelb gemeldet.
- **Statusleiste:** Mikro (Name, Pegel), Kabel (gefunden/fehlt), Mithören (an/aus), Puffer-
  Unterläufe-Zähler (für Fehlersuche).
- **Überlappung:** Standard „neuer Sound stoppt alten"; Option „überlagern".
- **Mikro stumm während Sound** (wie Soundpads „Stimme blockieren"): Option, Standard aus.
- **Tray:** Fenster schließen = minimieren in den Tray (das Mikro muss weiterlaufen);
  Beenden über Tray-Menü mit Hinweis „Discord hört dann Stille vom Kabel".

### 4.3 Audio-Technik der Durchleitung

- Mikro: WASAPI Capture, **shared**, Event-getrieben, Gerät per ID, Format = Mix-Format
  (typisch 48 kHz/Float). Kein Exklusivmodus, keine Formatänderung am Gerät.
- Kabel: WASAPI Render auf „CABLE Input", **shared**, Event-getrieben. Mix-Format des
  Kabels ist einstellbar (VB-CABLE-Standard 44,1 kHz); die App stellt es auf 48 kHz (2.6) und
  resampelt nur, wenn das nicht geht.
- Puffer: 10 ms Periode (Standard von WASAPI shared), App-Puffer 2–3 Perioden → 20–40 ms.
  Einstellbar „Puffer: klein/mittel/groß" für Rechner mit Aussetzern.
- Mixer: Float, Mikro + Sounds, Soft-Clipping/Limiter, damit laute Sounds nicht verzerren.
- Sounds werden beim Laden auf das Mix-Format dekodiert und im Speicher gehalten (Start ohne
  Verzögerung). Decoder: MP3/WAV via Media Foundation, OGG via NVorbis, FLAC via
  Media Foundation (Win 10+).
- Geräte-Hotplug: `IMMNotificationClient` — Kabel/Mikro verschwindet → gelbe Meldung, Stream
  neu starten, wenn es zurückkommt.

### 4.4 Nicht in v1

Ordner/Seiten, Suchfeld, Trimmen, Stimmverzerrer, Streamdeck, Mehrsprachigkeit, Themes,
Cloud-Sync, Mitschneiden, Autostart, Installer.

---

## 5. Technik-Stack

Randbedingung: Ein Agent schreibt, der Nutzer testet am Windows-Rechner. Also eine Sprache,
ein Build-Befehl, eine EXE, gute Windows-Audio-Bibliothek, MIT-verträglich.

**C# / .NET 10 (LTS), WPF, NAudio.** (.NET 8 läuft am 10.11.2026 aus, deshalb 10.)

| Baustein | Zweck | Lizenz |
|---|---|---|
| .NET 10 SDK, WPF | Oberfläche; `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true` → eine EXE | MIT |
| NAudio | WASAPI Capture/Render (shared, event-driven), `MixingSampleProvider`, Resampler, MP3/WAV/FLAC über Media Foundation, `MMDeviceEnumerator`, `IAudioEndpointVolume` | MIT |
| NVorbis | OGG-Decoder | MIT |
| CommunityToolkit.Mvvm | Bindings/Commands | MIT |
| H.NotifyIcon.Wpf oder Hardcodet.NotifyIcon | Tray | MIT / CPOL (**prüfen**, CPOL ist nicht OSI; im Zweifel H.NotifyIcon, MIT) |
| IPolicyConfig-Interop | nur fürs Zurückstellen; eigener Code nach dem **Muster** von SoundSwitch (SoundSwitch selbst ist GPL — nicht kopieren) | eigener Code |
| `RegisterHotKey` via P/Invoke | globale Hotkeys | — |
| Inno Setup | Installer zur fertigen Fassung | eigene freie Lizenz, auch kommerziell erlaubt |

Nicht genommen: Electron (groß, Audio nur über Umwege), Rust/Tauri (gut, aber
IPolicyConfig/COM-Interop umständlicher), Python (Packaging, Latenz, Hotkeys), Node.

Repo: MIT. Kein Treiber-Binary, kein VB-Audio-Material außer Link + Hinweistext. Prüf-Hook
gegen lokale Pfade und Namen.

---

## 6. Bauplan v1 (für den Code-Agenten)

### 6.1 Projektstruktur

```
<repo>/
  README.md                       Zweck, Einrichtung (Kabel, Discord, Krisp), Lizenzhinweis VB-CABLE
  LICENSE                         MIT
  src/
    Soundboard.Core/              keine UI-Abhängigkeit, testbar
      Audio/
        DeviceCatalog.cs          Endpunkte auflisten, per ID öffnen, Hotplug (IMMNotificationClient)
        CaptureSource.cs          Mikro, WASAPI shared, event-driven → Float-Samples
        SoundBank.cs              Laden/Dekodieren/Cachen der Sounds
        SoundPlayer.cs            Abspielen, Lautstärke, Stopp, Überlappung
        Mixer.cs                  Mikro + Sounds, Limiter, Resampling
        Sinks/
          ISink.cs                Push-Schnittstelle (Float, Kanäle, Rate)
          WasapiSink.cs           Render auf Endpunkt-ID (Kabel oder Kopfhörer)
          FileSink.cs             WAV schreiben — Testsenke ohne Hardware
        Engine.cs                 Graph aufbauen: Capture → Mixer → Kabel-Sink; Sounds → Monitor-Sink
      SystemState/
        AudioSnapshot.cs          Modell + JSON
        SnapshotService.cs        sichern, vergleichen, Bericht
        PolicyConfig.cs           IPolicyConfig-Interop, nur Restore
        DirtyFlag.cs
      Hotkeys/
        HotkeyService.cs          RegisterHotKey, Konflikte melden
      Config/
        Profile.cs                Kacheln, Hotkeys, Geräte-IDs, Puffergröße
    Soundboard.App/               WPF
      Views/ MainWindow, TileView, SetupWizard, StatusBar, Settings
      ViewModels/
      Tray/
      Cli.cs                      --check, --restore, --watch <pid>, --uninstall
  tests/
    Soundboard.Core.Tests/        Mixer, SoundBank, Snapshot-Vergleich (ohne Hardware)
  tools/
    pruefen.ps1                   Sperrliste (Pfade, Namen) vor Commit
```

### 6.2 Arbeitsschritte und was der Nutzer jeweils testet

Jeder Schritt endet mit einer lauffähigen EXE; der Nutzer testet, der Agent wartet auf
Rückmeldung. Discord wird erst in Schritt 7 gebraucht.

| # | Agent baut | Nutzer testet (ohne Discord bis Schritt 6) |
|---|---|---|
| 1 (erledigt) | Gerüst: Core + App, leeres Fenster, `DeviceCatalog`, Statusleiste zeigt alle Endpunkte mit IDs und Standard-Markierung; `--check` gibt Snapshot als Text aus | EXE starten, Geräteliste stimmt mit Windows-Sound überein? Nichts hat sich in Windows verändert? |
| 2 (erledigt) | `SoundBank`, `SoundPlayer`, Kacheln, Drag & Drop, Mithören-Senke auf gewähltem Kopfhörer, Stopp-alles, Lautstärke je Kachel | MP3/WAV/OGG reinziehen, abspielen, Lautstärke, Stopp. Kopfhörer per ID wählen; Windows-Standard bleibt unverändert |
| 3 (erledigt) | Hotkeys (Numpad), Konflikt-Meldung, Pausieren-Schalter | Hotkey belegen, in anderem Programm (Editor, Spiel) auslösen; Konflikt provozieren (zwei Kacheln gleiche Taste) |
| 4 (erledigt) | `CaptureSource` + `Mixer` + `FileSink`: Mikro + Sounds werden 10 s in eine WAV geschrieben | WAV anhören: Stimme + Sound sauber, kein Knacken, kein Übersteuern. Puffer klein/mittel/groß probieren |
| 5 (erledigt) | `SnapshotService`: sichern, vergleichen, Bericht; `PolicyConfig` Restore; Dirty-Flag; `--restore`; **Restore nur auf gesicherte Werte, Testfall: Nutzer stellt Standardgerät von Hand um, App meldet und stellt auf Knopfdruck zurück** | Snapshot machen, in Windows Standard-Wiedergabe absichtlich wechseln, App zeigt Abweichung, „Standardgerät zurückstellen" stellt es zurück. Danach von Hand prüfen |
| 6 (erledigt) | Kabel-Senke (`WasapiSink` auf „CABLE Input"), Kabel-Erkennung, Assistent Schritt 1 inkl. gelber Meldung, Snapshot vor Treiberinstallation, Vergleich danach | Erst **ohne** Kabel: gelbe Meldung, Knöpfe nicht grau. Dann VB-CABLE selbst installieren, Neustart, App erkennt Kabel, Vergleich zeigt, ob das Setup ein Standardgerät verstellt hat → zurückstellen. Pegelanzeige schlägt beim Sprechen aus. Gegenprobe ohne Discord: Windows-Sound → Aufnahme → „CABLE Output" zeigt Pegel |
| 7 (erledigt) | Assistent Schritte 2 + 3 (Discord-Anleitung, Freitext altes Mikro, Krisp-Hinweise), Tray, Wächter `--watch`, Beenden-Hinweis | Am Rechner mit Discord: Eingabe auf CABLE Output, Rauschunterdrückung aus, Testanruf: Gegenseite hört Stimme + Sound, Latenz ok? App beenden → Wächter-Hinweis erscheint, Discord-Mikro wieder auf altes Gerät → geht |
| 8 (erledigt) | `--uninstall`-Führung, README, Sperrlisten-Hook, Release-EXE | Kompletten Rückbau durchspielen: App weg, Kabel per VB-Setup weg, Geräte-Manager leer, Discord auf Default = altes Mikro, Snapshot-Vergleich grün |

Reihenfolge-Grundsatz: Alles, was am System etwas lesen oder schreiben kann (Schritt 5),
wird **vor** dem Kabel gebaut und getestet, damit die Rückstellung schon funktioniert, wenn
der Fremdtreiber kommt.

### 6.3 Regeln für den Code

- Geräte nur per ID; Standardgerät nur lesen (für Voreinstellung des Kopfhörers).
- Jeder Fehlzustand = gelbe Meldung mit dem konkreten fehlenden Schritt; nie `IsEnabled=false`.
- Keine Pfade, Namen, Projektbezüge in Kommentaren/Commits.
- Audio-Thread darf nicht allokieren oder blockieren (Sounds vorab dekodiert, Puffer fest).
- Snapshot-Vergleich ist Pflicht im Start- und Beendenpfad, Ergebnis immer sichtbar.

---

## 7. Namensvorschläge (offen)

1. **Tonbrett** — wörtlich „Soundboard", kurz, deutsch. Repo `tonbrett`. Favorit.
2. **Klangkachel** — beschreibt die Oberfläche. Repo `klangkachel`.
3. **Knallknopf** — frech, Airhorn-Kultur. Repo `knallknopf`.
4. **Lautkasten** — schlicht. Repo `lautkasten`.
5. **Pling** — kürzester Name, aber generisch und auf GitHub sicher belegt.

---

## 8. Offene Fragen

1. ~~Portable oder Installer?~~ Entschieden 03.11.2026: beim Entwickeln portable EXE, zur fertigen
   Fassung Installer (Inno Setup, pro Benutzer) plus portable EXE als zweiter Download.
2. **Name** (Abschnitt 7).
3. Sprachaktivierung oder Push-to-Talk in Discord? Bei PTT: reicht „Sound nur hörbar, solange
   PTT gehalten", oder soll eine zweite Discord-PTT-Taste empfohlen werden?
4. Soll das Mikro während eines Sounds optional stumm sein („Stimme blockieren") — in v1 oder
   später?
5. ~~Soll `release/0.17.0` in `main` zusammengeführt werden?~~ Erledigt 06.11.2026: `main` ist wieder
   der aktuelle Stand.

### 8.1 Offene Wünsche nach dem ersten Testtag (Stand 0.17.0, noch nicht begonnen)

1. **Eigene Kategorien wie Favoriten, zuklappbar:** anlegen, Pads hineinschieben, umsortieren,
   löschen mit Rückgängig; Zustand „zugeklappt“ gespeichert; Suche über alle Kategorien;
   Export/Import nimmt sie mit; Favoriten wird die erste Kategorie.
2. **Kategorie-Kopf:** schlichter kleiner Text mit Trennlinie bis zum Rand und kleinem Pfeil
   zum Zuklappen. Klick auf den Namen macht ihn bearbeitbar.
3. **Umbenennen bestätigen** (Pad und Kategorie): mit Enter **oder** Klick irgendwohin, Esc bricht ab.
4. **Pad ziehen animiert:** Das Pad hängt beim Ziehen am Mauszeiger, die anderen Pads gleiten zur
   Seite, beim Loslassen gleitet es in die Lücke, statt zu springen.
5. **Kräftigere Farben, zufällig ohne Muster:** sattere Pad-Palette (Text bleibt lesbar); neue
   Pads bekommen eine zufällige Farbe, nicht dieselbe wie das Pad daneben.
6. **Abspielleiste unten:** Wellenform aus dünnen senkrechten Strichen, darunter dünne Leiste
   mit hellem gespieltem Teil und rundem Knopf, links vergangene Zeit, rechts Gesamtzeit
   (mm:ss), Name des Sounds klein dazu; für den zuletzt gestarteten Sound; Klick springt im Sound.
7. **Stop-Knopf neu gestalten:** gefällt noch nicht. Erst mehrere Entwürfe zeigen (Ruhe und
   Spielen, hell und dunkel), dann den gewählten einbauen.

---

## 9. Bestehende Programme — was sie tun, woran sie scheitern

| Programm | Was es tut | Warum es Sound-Settings verbiegt / was wir lernen |
|---|---|---|
| **Voicemeeter** (VB-Audio) | Virtuelles Mischpult, mehrere virtuelle Ein-/Ausgänge | Muss als Standard-Wiedergabe-/Aufnahmegerät gesetzt werden; verlangt passende Abtastraten der echten Geräte; Neustarts; Reste im Geräte-Manager, Installer verweigert Neuinstallation ([Forum](https://forum.vb-audio.com/viewtopic.php?p=6962), [Streamscheme](https://www.streamscheme.com/?p=29185)). **Lehre:** nie Standardgeräte setzen, keine Formate an echten Geräten ändern. |
| **VB-CABLE** | nur das Kabel | Windows macht den zuletzt installierten Treiber ggf. zum Standard ([Handbuch](https://vb-audio.com/Cable/VBCABLE_ReferenceManual.pdf)); Uninstaller lässt Reste ([Forum](https://forum.vb-audio.com/viewtopic.php?p=6905)). **Lehre:** Snapshot vor/nach Installation, Nachkontrolle anleiten. |
| **Soundpad** (Steam, kostenpflichtig) | mischt Sounds direkt ins **Standard**-Aufnahmegerät, kein Kabel; „Stimme blockieren" | hängt am Standardgerät und an einem eigenen Treiberbaustein ([Handbuch](https://www.leppsoft.com/soundpad/help/manual/config/)). **Lehre:** bequemstes Modell, ohne signierten Treiber unerreichbar; „Stimme blockieren" als Option übernehmen. |
| **Resanance** (kostenlos, closed source) | Soundboard mit wählbaren Ausgängen | braucht in der Praxis VB-CABLE ([VoxBooster, Anbieterquelle](https://voxbooster.com/blog/soundboard-for-pc)); kein Rücksetz-Konzept. **Lehre:** Geräte per ID, Assistent für Discord. |
| **EXP Soundboard** (Open Source, Java) | Raster, Hotkeys, zwei Ausgänge | seit 2019 unbetreut, Java + VB-CABLE ([OSDN](https://pt.osdn.net/projects/sfnet_expsoundboard)). **Lehre:** zwei Ausgänge gleichzeitig (Mithören + Kabel) ist das richtige Modell; Laufzeitabhängigkeiten vermeiden. |
| **Voicemod** | Stimmverzerrer + Soundboard mit eigenem virtuellen Gerät | Anleitungen verlangen, Voicemod als Standard- **und** Kommunikationsgerät zu setzen; nach Deinstallation bleiben Geräte, Mikro muss von Hand zurück ([MS-Community](https://learn.microsoft.com/en-us/answers/a/8207178)). **Lehre:** genau der Zustand, den der Nutzer fürchtet. |
| **Discord-Soundboard** | eingebaut, 5,2 s, Slots je Server | kein Systemeingriff, aber zu kurz, keine Hotkeys. **Lehre:** Referenz für „nichts am System". |

Gemeinsames Muster: (1) sich zum Windows-Standard-/Kommunikationsgerät machen und es nicht
zurückstellen; (2) Treiber bleiben im Treiber-Store; (3) Windows-Updates mischen Geräte neu;
(4) echte Geräte auf passende Formate zwingen; (5) ohne Neustart verschwinden Geräte nicht.
Wir vermeiden (1) und (4) vollständig, überwachen (3), und lagern (2)/(5) mit Anleitung an den
Nutzer und VB-Audios eigenes Setup aus.

---

## Quellen

- VB-Audio: [Licensing](https://www.vb-audio.com/Services/licensing.htm) · [VBCABLE-Handbuch](https://vb-audio.com/Cable/VBCABLE_ReferenceManual.pdf) · [Shop/Download-Seite](https://shop.vb-audio.com/en/win-apps/11-vb-cable.html) · [Forum: Uninstall-Reste](https://forum.vb-audio.com/viewtopic.php?p=6905) · [Forum: 24H2, Pack 45](https://forum.vb-audio.com/viewtopic.php?p=7505) · [Forum: Voicemeeter-Neuinstallation](https://forum.vb-audio.com/viewtopic.php?p=6962)
- Alternativen: [VirtualDrivers/Virtual-Audio-Driver](https://github.com/VirtualDrivers/Virtual-Audio-Driver) · [HSpear/virtual-audio-wire](https://github.com/HSpear/virtual-audio-wire) · [Microsoft Audio-Driver-Samples](https://learn.microsoft.com/nl-nl/windows-hardware/drivers/samples/audio-driver-samples) · [OSR: Attestation-Signing](https://community.osr.com/discussion/comment/297791) · [MS Q&A: Signierung Virtual Audio Driver](https://learn.microsoft.com/en-in/answers/questions/1668206/i-fail-to-sign-a-windows-audio-driver-for-a-virtua) · [Thesycon TVirtAudio](https://thesycon.de/eng/virtual_audiodriver.shtml) · [AlternativeTo, Open Source](https://alternativeto.net/software/vb-audio-cable/?license=opensource)
- Discord: [Soundboard Guide](https://support.discord.com/hc/en-us/articles/12612888127767-Discord-Soundboard-Guide-Using-Adding-and-Managing-Sounds) · [Soundboard Developer-Doku](https://discord.com/developers/docs/resources/soundboard) · [Voice Connections / DAVE](https://docs.discord.com/developers/topics/voice-connections) · [Self-Bots](https://support.discord.com/hc/en-us/articles/115002192352-Automated-User-Accounts-Self-Bots) · [Platform-Manipulation](https://discord.com/safety/platform-manipulation-policy-explainer)
- Krisp/Soundboard: [simpleclean.app](https://simpleclean.app/blog/discord-noise-suppression-not-working) · [VoxBooster (Anbieter)](https://voxbooster.com/blog/voice-changer-voicemod-soundboard-doesnt-work-on-discord/)
- Anticheat: [Riot: Testsigning](https://support.riotgames.com/en-us/riot/security/restore-windows-security-to-normal-state) · [FACEIT AC](https://labs-docs.faceit.com/AntiCheat/GettingStarted/)
- IPolicyConfig: [SoundSwitch-Interop](https://github.com/Belphemur/SoundSwitch/blob/bbc1cb9cc5a70cd52aca2bc751218759015065e1/SoundSwitch.Audio.Manager/Interop/Client/ExtendedPolicyClient.cs) · [windows-rs #1355](https://github.com/microsoft/windows-rs/issues/1355) · [com-policy-config (Rust)](https://docs.rs/com-policy-config/latest/com_policy_config/struct.IPolicyConfig.html)
- Bestehende Programme: [Soundpad-Handbuch](https://www.leppsoft.com/soundpad/help/manual/config/) · [EXP Soundboard](https://pt.osdn.net/projects/sfnet_expsoundboard) · [Voicemod-Reste, MS-Community](https://learn.microsoft.com/en-us/answers/a/8207178) · [Streamscheme](https://www.streamscheme.com/?p=29185)
