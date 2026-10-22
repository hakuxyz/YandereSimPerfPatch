# Yandere Simulator Performance Patch (v1, Unity Mono Build)

Analysiert und gepatcht gegen **latest.zip** (Unity 6000.2.8f1, Mono Backend,
`Assembly-CSharp.dll` 5.169.664 Bytes). Ziel: deutlich flüssigeres Spiel auf
schwächerer Hardware (getestet gegen GTX 1050 Ti gedacht), ohne Inhalte zu
verändern.

## Was steckt im Patch? (12 Gruppen)

| # | Patch | Effekt |
|---|-------|--------|
| 1 | `StudentScript.Update` Distanz Throttle | Schüler über 30 m aktualisieren nur jede 2. Frame, über 70 m jede 3. Frame (gestaffelt über `StudentID`, Distanz Attribut bleibt jeden Frame frisch). Spart massiv CPU bei rund 80 Schülern. |
| 2 | `StudentScript.LateUpdate` Distanz Throttle | Animations Culling und Gewichte ebenfalls gedrosselt (gestaffelt, kein Doppel Read). |
| 3 bis 8 | `OptionGlobals` Defaults beim **ersten** Zugriff (nur wenn Key fehlt) | `Vsync=1`, `FPSIndex=2 (60 FPS)`, `DisableBloom=1`, `DisableObscurance=1`, `ParticleCount=2 (Niedrig)`, `LowDetailStudents=2 (20 m)`. Wird beim ersten Start geschrieben (`SetInt`+`Save`), danach normale Spiellogik. |
| 9 | Schatten **Weich zu Hart** (3 Stellen: `QualityManagerScript.UpdateShadows`, `NewSettingsScript.UpdateGraphics`, `BeatEmUpScript.Start`) | Sobald man Schatten einschaltet, laufen sie auf „hart" statt „weich" (rund die halben Shader Kosten). Default bleibt: Schatten aus. |
| 10 | `QualityManagerScript.Start`: Zeichnungsdistanz **350 auf 200** (nur bei Ersteinrichtung, `DrawDistanceLimit` bleibt 350) | Weniger sichtbare Geometrie, das entlastet GPU und CPU. |
| 11 | `QualityManagerScript.Start`: injizierter Sync `Profile.bloom/ambientOcclusion.enabled = !OptionGlobals.*` | Bloom und AO Default greifen auch, wenn das Spiel sonst nie das PP Profil neu einliest (Straße und Home regeln sich ohnehin selbst). |
| 12 | `DiscordRPC.UpdateActivity`: `Activity.Timestamps.Start` füllen (unix Sekunden, pro Sitzung einmalig gecacht) | Rich Presence zeigt die Spiel Laufzeit („seit X min"); das Original setzte nie einen Zeitstempel. Ohne laufendes Discord passiert nichts, die Presence bleibt einfach aus. |

Bewusst **nicht** gepatcht: DoF und MotionBlur (Defaults sind eh aus), FXAA
(billig, anlassen), Zeug Assets, Texturen und Szenen (kein
Redistributionsrisiko), Game Code Logik jenseits der Drossel.

## Anwendung

### Variante A: fertige DLL ersetzen (schnell)

1. Spiel einmal starten und beenden (legt evtl. erst die Registry und die
   PlayerPrefs an).
2. `Assembly-CSharp.dll` aus diesem Ordner nach
   `<Spiel>/YandereSimulator_Data/Managed/Assembly-CSharp.dll` kopieren
   (vorher sichern oder unten Variante B nutzen).
3. Fertig.

### Variante B: eigenen Build patchen (empfohlen bei Spiel Updates)

```bash
# frisch entpacktes Spiel oder neue Version:
dotnet YsPerfPatcher.dll "/pfad/zum/Spiel/YandereSimulator_Data/Managed/Assembly-CSharp.dll"
```

Der Befehl braucht die .NET Runtime oder das SDK, er läuft auch unter
Wine Pfaden, die DLL selbst ist plattformneutral. Beim ersten Lauf legt er
automatisch `Assembly-CSharp.dll.orig` daneben. Exit Codes: `0` gepatcht,
`2` schon gepatcht (idempotent), `3` Abbruch und die Datei bleibt unangetastet.
Nach jedem Spiel Update ist die DLL neu, dann den Patcher erneut ausführen.

### Zurücksetzen

```bash
cp Assembly-CSharp.dll.orig Assembly-CSharp.dll   # im Spiel Ordner
```

## Empfohlene Einstellungen (Ingame Optionen)

**Erstspieler (leere Registry):** die First Run Defaults oben greifen von
allein, 60 FPS und VSync, Bloom und AO aus, Partikel niedrig, Zeichnungsdistanz
200, LowPoly Schüler ab 20 m.

**Bestandsspieler** (Werte und Keys existieren bereits, die Defaults greifen
dann nicht):

| Option | Empfehlung |
|---|---|
| FPS Limit | **60** |
| VSync | **An** (bei Monitor über 60 Hz: **Aus**, sonst greift die VSync Rate die 60 Bremse aus) |
| Zeichnungsdistanz | **200** |
| Partikel | **Niedrig** |
| LowPoly und Detail Schüler | **an, 20 m** |
| Schatten | **Aus** für die maximale FPS Zahl, oder An, läuft dann auf **hart** |
| Bloom und AO (Obscurance) | **Aus** |
| Motion Blur und Tiefenschärfe | **Aus** |
| FXAA und Post AA | **An**, billig und glättet Kanten |
| Gras, Nebel Extra, Rimlight | **Aus** wie Default |

Notfalls, extrem wie YandereDevs eigener Debug Schlüssel: im Options Screen die
Taste **`l`** drücken. Das setzt Partikel, AO, Bloom, Zeichnungsdistanz und
mehr auf das Low Spec Preset. Danach die obigen Empfehlungen prüfen.

**Bestehende Werte entfernen, damit die Defaults greifen:** den Registry Key
`HKEY_CURRENT_USER\Software\YandereDev\YandereSimulator` löschen (unter Linux
im Wine Prefix `user.reg`, auf Windows in `regedit`). Betroffen wären dann alle
`Profile_0_*` Optionen, das Spiel richtet sich beim nächsten Start neu ein.

## Hinweise

1. Nur für den **lokalen Singleplayer Build**, es gibt weder Multiplayer noch
   Anticheat.
2. Die Drossel ist bewusst konservativ (30 m und 70 m): Spieler in
   Gesprächs und Kampfnähe laufen ungedrosselt weiter, die Reaktionslatenz über
   30 m beträgt höchstens 1 bis 2 Frames.
3. Spielerbewegung ist framebasiert, aber über die `deltaTime` Mathik,
   gedrosselte Fernschüler laufen trotzdem korrekt, nur etwas seltener pro
   Sekunde.
4. Nach Spiel Updates von YandereDev ist die DLL wieder original, dann den
   Patcher erneut laufen lassen (Variante B).
5. **Discord Rich Presence unter Wine und Linux:** die Windows DLL greift auf
   `\\.\pipe\discord-ipc-0` zu, dafür muss vor Spielstart die Brücke laufen
   (Paket `wine-discord-ipc-bridge`, z. B. als User Unit) und der Discord
   Desktop muss angemeldet sein. Ohne beides schlägt nur die Presence fehl,
   das Spiel läuft normal.
6. `src/YsPerfPatcher/` enthält den kompletten Quellcode (Mono.Cecil 0.11.6,
   net10.0), gebaut wird mit `dotnet build -c Release`.

## Verifikation

1. 12 von 12 Patchgruppen, zweiter Lauf idempotent (Exit 2), Ausgabe
   deterministisch, byte identisch.
2. Geparste und gedeompilierte End zu End Prüfung der ganzen DLL (ILSpy, Exit 0).
3. Quellcode Analyse der betroffenen Methoden (`ilspycmd` und Cecil) gegen den
   Original Build (`Assembly-CSharp.dll.orig`).
