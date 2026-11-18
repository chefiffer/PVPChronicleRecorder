# PVP Chronicle Recorder · OBS-Anleitung

Diese Anleitung beschreibt die OBS-Einrichtung für automatische PvP-Aufnahmen sowie Lösungen für Verbindungsfehler, falsche Fenster, schwarze Ränder, große Dateien und unerwünschten Desktop-Ton.

> [!IMPORTANT]
> Recorder steuert nur OBS und verknüpft lokale Aufnahmen. PVP Chronicle, PVP Chronicle Recorder und OBS Studio müssen installiert und aktiv sein.

## 1. OBS WebSocket aktivieren

1. OBS starten und **Werkzeuge → WebSocket-Servereinstellungen** öffnen.
2. Server aktivieren, Port `4455` beibehalten und ein nur lokal verwendetes Passwort setzen.
3. Speichern und OBS geöffnet lassen.

Port 4455 niemals ins Internet weiterleiten oder für andere Rechner freigeben. In `/pvprec` eintragen:

```text
OBS-WebSocket-Adresse: ws://127.0.0.1:4455
OBS-WebSocket-Passwort: das in OBS festgelegte Passwort
```

Speichern und **OBS-Verbindung testen** wählen. Erwartet wird „OBS WebSocket verbunden“.

## 2. Eigene Aufnahmeszene

Eine Szene wie „FFXIV PvP-Aufnahme“ verhindert, dass Kamera, Browser und Stream-Hinweise mitaufgenommen werden.

### Variante A: Spielaufnahme

Über `+` **Spielaufnahme** hinzufügen, **Bestimmtes Fenster aufnehmen** wählen und „FINAL FANTASY XIV“ von `ffxiv_dx11.exe` auswählen. Bei wiederholt falschem Fenster die Quelle neu erstellen oder Bildschirmaufnahme verwenden.

### Variante B: Bildschirmaufnahme

Sie ist zuverlässiger, nimmt aber alles Sichtbare auf. Den FFXIV-Bildschirm auswählen und auf den tatsächlichen Spielbereich zuschneiden.

## 3. Schwarze Ränder ohne Verzerrung entfernen

`px` bedeutet Pixel. Bei `1920×1080` und `120 px` unten gilt:

```text
1080 - 120 = 960 px
```

Quelle wählen → **Transformieren → Transformation bearbeiten** → unten `120` zuschneiden. Unter **Einstellungen → Video** Basis- und Ausgabeauflösung auf `1920×960` oder dieselbe Seitenrelation stellen, danach **An Bildschirm anpassen**. Alternativ mit `Alt` ziehen. Nicht **Auf Bildschirm strecken** verwenden. Eine 5–10-sekündige Testdatei prüfen.

## 4. Nur Spielton aufnehmen

Desktop-Audio 1/2 und unnötige Mikrofone unter **Einstellungen → Audio** deaktivieren. **Anwendungsaudioaufnahme (Beta)** hinzufügen und `ffxiv_dx11.exe` wählen. Spiel- und Browserton gleichzeitig testen: Nur der Spielpegel darf ausschlagen. Nach einem Absturz das neue FFXIV-Fenster erneut wählen, falls der Ton fehlt.

## 5. Format und Dateigröße

| Option | Empfehlung | Grund |
| --- | --- | --- |
| Format | MKV | Übersteht Abstürze eher als MP4 |
| Encoder | NVENC H.264, falls verfügbar | Weniger CPU-Last, gute Kompatibilität |
| Qualität | Hohe Qualität, mittlere Größe | Für Replay ausreichend |
| Audio | 128 oder 160 Kbps | Für Spielton ausreichend |
| Bildrate | 30 FPS | Deutlich kleinere Dateien |

6000 Kbps Video plus 160 Kbps Audio ergeben etwa 46 MB pro Minute. Für kleinere Dateien 3500–5000 Kbps verwenden, im erweiterten Modus CQP ab `CQ 24` testen, 30 FPS beibehalten und schwarze Ränder nicht kodieren. Höheres CQ bedeutet kleinere Dateien und weniger Qualität. Nach Änderungen eine Minute Kampf aufnehmen und Größe sowie Textschärfe prüfen.

## 6. Recorder konfigurieren

In `/pvprec` Sprache wählen, automatische Aufnahme aktivieren, `ws://127.0.0.1:4455` und OBS-Passwort eintragen. Den Flyleaf-FFmpeg-Ordner normalerweise leer lassen, Erstbild-Kalibrierung zunächst auf `0 ms` stellen, speichern, Verbindung testen und die offizielle Laufzeit installieren. Download, SHA-256-Prüfung und Entpacken müssen abgeschlossen sein.

Die Kalibrierung korrigiert nur eine reproduzierbare feste Abweichung. Alle vier Eingaben besitzen sichtbare Namen und Erklärungen. Die Qualitätswahl skaliert lediglich das aktuelle dekodierte Bild: Es wird kein zweites Video erzeugt und kein Proxy-Cache auf C: abgelegt. Beim Schließen wird der Arbeitsspeicher freigegeben; OBS-Original und FFmpeg bleiben erhalten.

## 7. Absturz und Wiederverbindung

- Reguläres Ende: OBS wird gestoppt und das Video zugeordnet.
- Absturz: Endereignis kann fehlen; OBS und Wiederherstellungsstatus können abweichen.
- Wiederverbindung: Neustart und Zusammenfügen sind nicht garantiert.
- Nächstes Match: neue, unabhängige Aufnahme.

Nach einem Absturz OBS prüfen und eine Aufnahme des Login-Bildschirms manuell stoppen. Benötigte Originale nicht löschen.

## Sicherheit und Datenschutz

- WebSocket-Port und Passwort niemals öffentlich zugänglich machen.
- Videos können Namen, Chat, Hinweise und Desktop-Inhalte enthalten; vor dem Teilen prüfen.
- Recorder lädt nichts hoch. Manuelle Cloud- oder GitHub-Uploads liegen außerhalb seiner Kontrolle.
