import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";
import { createRequire } from "module";
import plugin from "./main.js";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const require = createRequire(import.meta.url);
const configPath = path.join(__dirname, "config.json");

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

const buttons = ["undo","new-tline","cue","run","dump","go-to-end","input-view","multi-src","single-clip","trim-all","ripl-del","video-only","auto-sting","ramp-down","speed-pos","sync-bin","add-mark","full-view","audio-only","sel-sting","slow","set-speed","split","snap","mv-view","source","timeline","live-speed","smart-insert","appnd","ripl-owr","close-up","place-on-top","src-owr","set-poi","go-to-poi","2sec","3sec","4sec","5sec","6sec","7sec","in","out","all-cams","cams-9-16","title1","title2","title3","title4","title5","title6","trim-in","trim-out","roll","slip","slide","trans-dur","cam1","cam2","cam3","cam4","cam5","cam6","cam7","cam8","cut","dis","trans","stop-play"];
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
let accum = 0;

function getStepSize() {
    // Rychlost 10 = prah 1400 (presne jako puvodni SLOW)
    if (mode === "slow") return 14000 / cfg.slowSpeed;
    if (mode === "jog")  return 14000 / cfg.jogSpeed;
    return 14000 / cfg.scrlSpeed;
}

function updateModeLeds() {
    if (!surfaceInstance) return;
    surfaceInstance.draw(0, { controlId: "slow-jog", color: mode === "slow" ? "#ffffff" : "#000000" });
    surfaceInstance.draw(0, { controlId: "jog-jog",  color: mode === "jog"  ? "#ffffff" : "#000000" });
    surfaceInstance.draw(0, { controlId: "scrl-jog", color: mode === "scrl" ? "#ffffff" : "#000000" });
}

setInterval(() => {
    const stepSize = getStepSize();
    let steps = Math.trunc(accum / stepSize);
    if (steps !== 0) {
        accum -= steps * stepSize;
        steps = Math.max(-500, Math.min(500, steps));
        console.log("WHEEL|" + steps);
    }
}, 15);

const hostCallbacks = {
    disconnect: () => {
        console.log("STATUS|DISCONNECTED");
        process.exit(3);
    },
    keyDownById: (id) => {
        if (id === "slow-jog") { mode = "slow"; accum = 0; updateModeLeds(); return; }
        if (id === "jog-jog")  { mode = "jog";  accum = 0; updateModeLeds(); return; }
        if (id === "scrl-jog") { mode = "scrl"; accum = 0; updateModeLeds(); return; }

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
            // Pri zmene smeru otaceni vynulujeme zbytek, at kolecko reaguje okamzite
            if ((val > 0 && accum < 0) || (val < 0 && accum > 0)) accum = 0;
            accum += val;
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