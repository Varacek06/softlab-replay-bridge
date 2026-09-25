# Blackmagic Replay Editor – SoftLab-NSK Bridge

This repository contains software to connect the professional USB panel **Blackmagic DaVinci Resolve Replay Editor** (and Speed Editor) with the **SoftLab-NSK** broadcast and replay system (Forward Replay, Forward GoLive, PostPlay) via a virtual MIDI interface.

The bridge acts as a lightweight, portable System Tray application for Windows, translating the proprietary USB HID signals of the panel into standard MIDI commands that SoftLab-NSK natively understands.

---

## Main Features

*   **Zero Lag Wheel Response:** The first movement of the wheel (after idle time or direction change) instantly sends 1 frame, ensuring immediate reaction to the slightest touch.
*   **Three Hardware JOG Wheel Modes:**
    *   `JOG` (default speed 10): 1:1 mode for standard and precise frame-by-frame scrubbing.
    *   `SLOW` (default speed 3): High-resolution fine mode for precise detail assessment.
    *   `SCRL` (speed 350): Fast timeline scroll with non-linear acceleration and smooth deceleration (runs on a 100Hz loop, avoiding video stutter).
*   **T-Bar Fader:** Smooth control of playback speed (`MIDIHandle_TBAR_Event`) with a functional LED scale directly on the panel.
*   **Button Detection:** Panel buttons (CUE, RUN, IN, OUT, etc.) transmit standard MIDI notes, easily mappable in SoftLab via the *Detect* function.
*   **Standalone Operation:** The compiled application runs in the background and does not require Node.js to be installed globally on the system.

---

## Requirements

*   **Operating System:** Windows 10 or Windows 11 (64-bit).
*   **Hardware:** Blackmagic Replay Editor connected via USB. *(Note: DaVinci Resolve or Bitfocus Companion must not be running during use, as they may request exclusive access to the device.)*

---

## Installation Guide

1. Download the latest installation archive `ReplayEditor_Setup.zip` from the *Releases* tab here on GitHub and extract it.
2. Run the **`SETUP.bat`** file as an Administrator (Right-click -> *Run as administrator*).
3. The setup script will automatically handle everything:
    * It will **ask you to manually install the loopMIDI utility** (if missing). A webpage will open automatically.
    * Copies files to `C:\SoftLab_ReplayBridge\`.
    * Compiles the control application `ReplayBridge.exe`.
    * Registers the necessary settings into the SoftLab registry.
    * Creates a desktop shortcut and immediately launches the application.
4. **First Launch (Important):**
    * At the end of the installation, the **loopMIDI** window will open.
    * Type exactly `loopMIDI Port` into the bottom name field.
    * Click the **+** button (the port will be created and added to the list).
    * In the loopMIDI settings (right-click its icon in the system tray), check the **Autostart loopMIDI** and **Start minimized** options. You're done!

---

## SoftLab-NSK Configuration

Open the GPI console configuration in SoftLab (e.g., via the *FDOnAir* or *SLGPI Server* application) and proceed as follows:

1. Select the **External GPI Console #1** configuration (or another free console depending on your license).
2. Set the device type to **MIDI** and set the port to `loopMIDI Port`.
3. In the Assignment section, set:
    *   **Jog:** Select the `MIDIWheel_JogShuttle` event.
    *   **Work slow speed:** Select the `MIDIHandle_TBAR_Event` event.
4. In the buttons section, map functions as needed by clicking **Detect** and pressing the corresponding button on the physical panel. SoftLab will recognize the event (e.g., as `MIDIKey_cue_DOWN`).
5. Save the settings and restart the GPI server service.

---

## Wheel Sensitivity Configuration

The wheel's rotation speed and direction can be adjusted by the user at any time:
1. Double-click the **green bridge icon** in the system tray to open the configuration dialog.
2. Set your desired values for *SLOW JOG*, *JOG*, and *SCRL*, or check the *Invert Jog* option to reverse the rotation direction.
3. Click **Save and Apply**. The application will apply the changes immediately, without needing a restart.

---

## Troubleshooting

The system tray icon indicates the current communication status:
*   🟢 **Green:** Everything is working correctly.
*   🔴 **Red ("Waiting for loopMIDI Port..."):** Check if the loopMIDI utility is running and the port is named exactly `loopMIDI Port`.
*   🟠 **Orange ("Waiting for USB connection..."):** Check the panel's connection to the PC.
*   🔴 **Red ("Device is blocked"):** The panel is likely blocked by another software (e.g., DaVinci Resolve). Close it and give the bridge a moment to restore the connection.
