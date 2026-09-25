# Blackmagic Replay Editor – SoftLab-NSK Bridge

*(Scroll down for English version)*

Tento repozitář obsahuje software pro propojení profesionálního USB pultu **Blackmagic DaVinci Resolve Replay Editor** (případně i Speed Editor) s vysílacím a odbavovacím systémem **SoftLab-NSK** (Forward Replay, Forward GoLive, PostPlay) přes virtuální MIDI rozhraní.

Most funguje jako lehká "System Tray" aplikace pro Windows, která na pozadí překládá proprietární USB HID signály pultu na standardní MIDI příkazy, kterým SoftLab-NSK nativně rozumí.

---

## Hlavní funkce

*   **Rychlá odezva kolečka:** První pohyb kolečka (po nečinnosti nebo změně směru) okamžitě odesílá 1 snímek, což zajišťuje okamžitou reakci i na ten nejjemnější dotyk.
*   **Tři hardwarové režimy JOG kolečka:**
    *   `JOG` (výchozí rychlost 10): Režim 1:1 pro standardní a přesné posouvání po snímcích.
    *   `SLOW` (výchozí rychlost 3): Jemný režim s vysokým rozlišením pro detailní zkoumání.
    *   `SCRL` (rychlost 350): Rychlý posun na časové ose s plynulým zrychlením a zpomalením.
*   **T-Bar páka:** Plynulé ovládání rychlosti přehrávání (`MIDIHandle_TBAR_Event`) s funkční LED škálou přímo na pultu.
*   **Detekce tlačítek:** Tlačítka pultu (CUE, RUN, IN, OUT atd.) vysílají standardní MIDI noty, které lze v SoftLabu snadno namapovat přes funkci *Detect*.
*   **Automatický virtuální MIDI port:** Aplikace sama vytváří svůj MIDI port pomocí driveru teVirtualMIDI, není potřeba nastavovat ručně porty v programech jako loopMIDI.

---

## Požadavky

*   **Operační systém:** Windows 10 nebo Windows 11 (64-bit).
*   **Hardware:** Pult Blackmagic Replay Editor připojený přes USB. *(Upozornění: Během používání nesmí běžet DaVinci Resolve ani Bitfocus Companion, mohly by blokovat komunikaci s pultem.)*
*   **Driver:** Nainstalovaný driver **teVirtualMIDI** (případně program loopMIDI, který tento driver obsahuje).

---

## Instalace

1. Stáhněte si nejnovější instalační archiv z *Releases* na GitHubu a rozbalte jej.
2. Spusťte soubor **`SETUP.bat`** a případně potvrďte žádost o oprávnění správce.
3. Instalační skript zařídí vše potřebné:
    * Zkopíruje soubory do složky programu (`C:\Program Files\SoftLab_ReplayBridge`).
    * Zkompiluje řídící C# aplikaci `ReplayBridge.exe` bez zbytečných závislostí.
    * Zaregistruje potřebná nastavení do SoftLab registrů.
    * Vytvoří zástupce na ploše a rovnou aplikaci spustí.

---

## Konfigurace v SoftLab-NSK

1. V programu *SLGPI Server* vyberte nastavení pro konzoli (např. **External GPI Console #1**).
2. Typ zařízení nastavte na **MIDI** a port nastavte na `SoftLab ReplayBridge` (případně jiný, pokud používáte záložní WinMM metodu).
3. V sekci přiřazení:
    *   **Jog:** Zvolte událost `MIDIWheel_JogShuttle`.
    *   **Work slow speed:** Zvolte událost `MIDIHandle_TBAR_Event`.
4. Pomocí tlačítka **Detect** namapujte konkrétní klávesy pultu na požadované funkce.
5. Uložte nastavení a restartujte GPI službu.

---

## Nastavení aplikace

Citlivost kolečka a směr můžete kdykoliv změnit:
1. Dvakrát klikněte na **ikonu bridge** (zelená, oranžová nebo červená tečka) u hodin.
2. Nastavte požadované hodnoty pro *SLOW JOG*, *JOG*, *SCRL* a případně invertujte směr kolečka.
3. Klikněte na **Save and Apply**. Změny se projeví okamžitě.

---

## Řešení problémů (Indikace stavu)

Ikona v oznamovací oblasti (u hodin) zobrazuje aktuální stav komunikace:
*   🟢 **Zelená (Info):** Vše funguje správně, USB pult je připojen.
*   🔴 **Červená (Error):** Čeká se na vytvoření nebo připojení MIDI driveru (teVirtualMIDI / loopMIDI).
*   🟠 **Oranžová (Warning):** MIDI běží, ale čeká se na připojení pultu do USB.
*   🔴 **Červená (Blokováno):** Pult je připojen do USB, ale je blokován jiným softwarem (např. DaVinci Resolve). Vypněte ho.

---
---

# (English Version) Blackmagic Replay Editor – SoftLab-NSK Bridge

This repository contains software to connect the professional USB panel **Blackmagic DaVinci Resolve Replay Editor** (and Speed Editor) with the **SoftLab-NSK** broadcast and replay system (Forward Replay, Forward GoLive, PostPlay) via a virtual MIDI interface.

The bridge acts as a lightweight, portable System Tray application for Windows, translating the proprietary USB HID signals of the panel into standard MIDI commands that SoftLab-NSK natively understands.

---

## Main Features

*   **Zero Lag Wheel Response:** The first movement of the wheel instantly sends 1 frame, ensuring immediate reaction.
*   **Three Hardware JOG Wheel Modes:**
    *   `JOG` (default 10): 1:1 mode for standard frame-by-frame scrubbing.
    *   `SLOW` (default 3): High-resolution fine mode for precise detail assessment.
    *   `SCRL` (speed 350): Fast timeline scroll with non-linear acceleration.
*   **T-Bar Fader:** Smooth control of playback speed (`MIDIHandle_TBAR_Event`) with a functional LED scale directly on the panel.
*   **Standalone Operation:** The compiled application runs in the background. It uses native teVirtualMIDI integration to spawn a dedicated MIDI port ("SoftLab ReplayBridge") automatically.

---

## Requirements

*   **Operating System:** Windows 10 or Windows 11 (64-bit).
*   **Hardware:** Blackmagic Replay Editor connected via USB. *(Note: DaVinci Resolve or Bitfocus Companion must not be running during use.)*
*   **Driver:** Tobias Erichsen's **teVirtualMIDI** driver installed (or loopMIDI).

---

## Installation Guide

1. Download the latest installation archive from the *Releases* tab on GitHub and extract it.
2. Run the **`SETUP.bat`** file (it will automatically ask for Administrator privileges).
3. The setup script will handle everything:
    * Copies files to `C:\Program Files\SoftLab_ReplayBridge\`.
    * Compiles the control application `ReplayBridge.exe`.
    * Registers settings into the SoftLab registry.
    * Creates a desktop shortcut and launches the application.

---

## Configuration & Troubleshooting

Double-click the tray icon to open the **Wheel Speed Settings**.

The system tray icon indicates the current communication status:
*   🟢 **Green:** Everything is working correctly.
*   🔴 **Red ("Waiting for loopMIDI/teVirtualMIDI..."):** Ensure the teVirtualMIDI driver is installed.
*   🟠 **Orange ("Waiting for USB connection..."):** Check the panel's connection to the PC.
*   🔴 **Red ("Device is blocked"):** Close Resolve or Companion to release the USB panel.
