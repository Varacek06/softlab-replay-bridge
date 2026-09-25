import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";
import { createRequire } from "module";
import plugin from "./main.js";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const require = createRequire(import.meta.url);
const configPath = path.join(__dirname, "config.json");

// Vychozi rychlosti (upravitelne dvojklikem na ikonku u hodin)
let cfg = { slowSpeed: 3, jogSpeed: 10, scrlSpeed: 350, invertWheel: false };

function loadConfig() {
    try {
        if (fs.existsSync(configPath)) {
            const raw = JSON.parse(fs.readFileSync(configPath, "utf8"));
            cfg.slowSpeed = Math.max(1, Number(raw.slowSpeed) || 3);
            cfg.jogSpeed  = Math.max(1, Number(raw.jogSpeed)  || 10);
            cfg.scrlSpeed = Math.max(1, Number(raw.scrlSpeed) || 350);
        }
    } catch (e) {}
}
loadConfig();
setInterval(loadConfig, 500);

const buttons = [
    "undo","new-tline","cue","run","dump","go-to-end","input-view","multi-src","single-clip",
    "trim-all","ripl-del","video-only","auto-sting","ramp-down","speed-pos","sync-bin","add-mark","full-view",
    "audio-only","sel-sting","slow","set-speed","split","snap","mv-view","source","timeline",
    "live-speed","smart-insert","appnd","ripl-owr","close-up","place-on-top","src-owr","set-poi","go-to-poi",
    "2sec","3sec","4sec","5sec","6sec","7sec",
    "in","out","all-cams","cams-9-16","title1","title2","title3","title4","title5","title6",
    "trim-in","trim-out","roll","slip","slide","trans-dur",
    "cam1","cam2","cam3","cam4","cam5","cam6","cam7","cam8",
    "cut","dis","trans","stop-play"
];
const buttonMap = {};
for (let i = 0; i < buttons.length; i++) buttonMap[buttons[i]] = i + 10;

const prebuildDir = path.join(__dirname, "prebuilds", "HID-win32-x64");
const nodeFile = fs.readdirSync(prebuildDir).find(f => f.endsWith(".node"));
const hidBinding = require(path.join(prebuildDir, nodeFile));

const devices = hidBinding.devices();
const candidates = devices.filter(d => d.vendorId === 7899 && d.productId === 55825 && d.path);
if (candidates.length === 0) {
    console.log("STATUS|WAIT_USB");
    process.exit(2);
}

let surfaceInstance = null;
let mode = "jog";

// 1 fyzicky mikrokrok optickeho enkoderu Replay Editoru = 360
const ENCODER_UNIT = 360;

let tickAccum = 0;      // Akumulator pro SLOW a JOG (v jednotkach 360)
let scrlVelocity = 0;   // Aktualni plynula rychlost pro SCRL
let scrlSubFrame = 0;   // Desetinny akumulator pro plynuly SCRL
let lastInputTime = 0;
let lastDir = 0;

function updateModeLeds() {
    if (!surfaceInstance) return;
    surfaceInstance.draw(0, { controlId: "slow-jog", color: mode === "slow" ? "#ffffff" : "#000000" });
    surfaceInstance.draw(0, { controlId: "jog-jog",  color: mode === "jog"  ? "#ffffff" : "#000000" });
    surfaceInstance.draw(0, { controlId: "scrl-jog", color: mode === "scrl" ? "#ffffff" : "#000000" });
}

// 100Hz smycka (kazdych 10 ms) pro naprosto plynuly chod bez skubani dekoderu
setInterval(() => {
    const idleMs = Date.now() - lastInputTime;

    if (mode === "scrl") {
        // Pokud se kolecko netoci, plynule brzdi do nuly
        if (idleMs > 35) {
            scrlVelocity *= 0.72;
            if (Math.abs(scrlVelocity) < 0.08) {
                scrlVelocity = 0;
                scrlSubFrame = 0;
                return;
            }
        }
        scrlSubFrame += scrlVelocity;
        let steps = Math.trunc(scrlSubFrame);
        if (steps !== 0) {
            scrlSubFrame -= steps;
            // Max 6 snimku za 10 ms (= 600 snimku/s = 24x realna rychlost zcela plynule)
            steps = Math.max(-6, Math.min(6, steps));
            console.log("WHEEL|" + steps);
        }
        return;
    }

    // Pro SLOW a JOG: po zastaveni ruky vycistime zbytek, aby byl dalsi dotyk 100% okamzity
    if (idleMs > 120) {
        tickAccum = 0;
        return;
    }

    // Kolik 360-ti kroku je potreba na 1 snimek pri souvislem toceni:
    // JOG (jogSpeed=10) -> 4.0 zoubky na snimek (1440, presne jako puvodni SLOW)
    // SLOW (slowSpeed=3) -> ~13.3 zoubku na snimek
    const ticksPerFrame = mode === "slow" ? (40 / cfg.slowSpeed) : (40 / cfg.jogSpeed);

    let steps = Math.trunc(tickAccum / ticksPerFrame);
    if (steps !== 0) {
        tickAccum -= steps * ticksPerFrame;
        steps = Math.max(-6, Math.min(6, steps));
        console.log("WHEEL|" + steps);
    }
}, 10);

const hostCallbacks = {
    disconnect: () => {
        console.log("STATUS|DISCONNECTED");
        process.exit(3);
    },
    keyDownById: (id) => {
        if (id === "slow-jog") { mode = "slow"; tickAccum = 0; scrlVelocity = 0; updateModeLeds(); return; }
        if (id === "jog-jog")  { mode = "jog";  tickAccum = 0; scrlVelocity = 0; updateModeLeds(); return; }
        if (id === "scrl-jog") { mode = "scrl"; tickAccum = 0; scrlVelocity = 0; updateModeLeds(); return; }

        if (surfaceInstance) {
            if (id.startsWith("cam")) {
                for (let c = 1; c <= 8; c++) {
                    surfaceInstance.draw(0, { controlId: "cam" + c, color: id === ("cam" + c) ? "#ffffff" : "#000000" });
                }
            } else {
                surfaceInstance.draw(0, { controlId: id, color: "#ffffff" });
            }
        }
        const note = buttonMap[id];
        if (note !== undefined) console.log("ON|" + note + "|" + id);
    },
    keyUpById: (id) => {
        if (id === "slow-jog" || id === "jog-jog" || id === "scrl-jog") return;
        if (surfaceInstance && !id.startsWith("cam")) {
            surfaceInstance.draw(0, { controlId: id, color: "#000000" });
        }
        const note = buttonMap[id];
        if (note !== undefined) console.log("OFF|" + note);
    },
    sendVariableValue: (name, val) => {
        if (name === "tbarValueVariable") {
            const midiVal = Math.max(0, Math.min(127, Math.round(val * 127)));
            console.log("CC|1|" + midiVal);
            if (surfaceInstance) surfaceInstance.onVariableValue("tbarLeds", Math.round(val * 16));
        } else if (name === "jogVelocityVariable") {
            if (val === 0) return;
            const now = Date.now();
            const dir = val > 0 ? 1 : -1;
            const wasIdle = (now - lastInputTime) > 120;
            const dirChanged = (dir !== lastDir);

            lastInputTime = now;
            lastDir = dir;

            // Prepocet surove hodnoty na pocet fyzickych 360-ti zoubku
            const rawTicks = val / ENCODER_UNIT;

            if (mode === "scrl") {
                if (dirChanged) {
                    scrlVelocity = 0;
                    scrlSubFrame = 0;
                }
                // Plynula nelinearni akcelerace: pomale toceni = jemne, rychle = plynuly sprint
                const scale = cfg.scrlSpeed / 100;
                const accel = dir * Math.pow(Math.abs(rawTicks), 1.25) * 0.35 * scale;
                scrlVelocity = (scrlVelocity * 0.55) + (accel * 0.45);
                return;
            }

            // SLOW a JOG: Okamzity 1 snimek pri prvnim malickem pohybu (360) po pauze nebo zmene smeru
            if (wasIdle || dirChanged) {
                tickAccum = 0;
                console.log("WHEEL|" + dir);
                return;
            }

            tickAccum += rawTicks;
        }
    }
};

let openedOk = false;
for (const cand of candidates) {
    try {
        const opened = await plugin.openSurface("replay", { path: cand.path }, hostCallbacks);
        surfaceInstance = opened.surface;
        await surfaceInstance.init();
        updateModeLeds();
        openedOk = true;
        console.log("STATUS|READY");
        break;
    } catch (e) {}
}

if (!openedOk) {
    console.log("STATUS|BUSY");
    process.exit(4);
}
setInterval(() => {}, 60000);