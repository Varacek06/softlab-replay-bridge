import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";
import { createRequire } from "module";
import plugin from "./main.js";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const require = createRequire(import.meta.url);
const localConfigPath = path.join(__dirname, "config.json");
const appDataConfigPath = process.env.APPDATA
    ? path.join(process.env.APPDATA, "SoftLabReplayBridge", "config.json")
    : null;

// 1 physical microstep of the optical encoder = 360
export const ENCODER_UNIT = 360;

export const DEFAULT_CFG = {
    slowSpeed: 3,
    jogSpeed: 10,
    scrlSpeed: 350,
    invertWheel: false,
    jogCurveEnabled: true,
    jogCurveThreshold: 30,
    jogCurveRate: 0.015,
    jogCurveMaxMultiplier: 20,
    shuttleDoubleClickMs: 350,
    shuttleSensitivity: 80,
    shuttleDeadzone: 0.03,
    shuttleMidi14Bit: false
};

function numOr(val, fallback, min, max = Infinity) {
    const n = Number(val);
    if (!Number.isFinite(n)) return fallback;
    return Math.max(min, Math.min(max, n));
}

export function mergeConfigObject(target, raw) {
    if (!raw || typeof raw !== "object") return target;
    if (raw.slowSpeed !== undefined) target.slowSpeed = numOr(raw.slowSpeed, target.slowSpeed, 0.1);
    if (raw.jogSpeed !== undefined) target.jogSpeed = numOr(raw.jogSpeed, target.jogSpeed, 0.1);
    if (raw.scrlSpeed !== undefined) target.scrlSpeed = numOr(raw.scrlSpeed, target.scrlSpeed, 0.1);
    if (typeof raw.invertWheel === "boolean") target.invertWheel = raw.invertWheel;
    if (typeof raw.jogCurveEnabled === "boolean") target.jogCurveEnabled = raw.jogCurveEnabled;
    if (raw.jogCurveThreshold !== undefined) target.jogCurveThreshold = numOr(raw.jogCurveThreshold, target.jogCurveThreshold, 0);
    if (raw.jogCurveRate !== undefined) target.jogCurveRate = numOr(raw.jogCurveRate, target.jogCurveRate, 0);
    if (raw.jogCurveMaxMultiplier !== undefined) target.jogCurveMaxMultiplier = numOr(raw.jogCurveMaxMultiplier, target.jogCurveMaxMultiplier, 1);
    if (raw.shuttleDoubleClickMs !== undefined) target.shuttleDoubleClickMs = numOr(raw.shuttleDoubleClickMs, target.shuttleDoubleClickMs, 50);
    if (raw.shuttleSensitivity !== undefined) target.shuttleSensitivity = numOr(raw.shuttleSensitivity, target.shuttleSensitivity, 1);
    if (raw.shuttleDeadzone !== undefined) target.shuttleDeadzone = numOr(raw.shuttleDeadzone, target.shuttleDeadzone, 0, 0.45);
    if (typeof raw.shuttleMidi14Bit === "boolean") target.shuttleMidi14Bit = raw.shuttleMidi14Bit;
    return target;
}

export function loadMergedConfig(localPath = localConfigPath, appDataPath = appDataConfigPath) {
    const next = { ...DEFAULT_CFG };
    for (const filePath of [localPath, appDataPath]) {
        if (!filePath) continue;
        try {
            if (fs.existsSync(filePath)) {
                const raw = JSON.parse(fs.readFileSync(filePath, "utf8"));
                mergeConfigObject(next, raw);
            }
        } catch (e) {}
    }
    return next;
}

export const buttons = ["undo","new-tline","cue","run","dump","go-to-end","input-view","multi-src","single-clip","trim-all","ripl-del","video-only","auto-sting","ramp-down","speed-pos","sync-bin","add-mark","full-view","audio-only","sel-sting","slow","set-speed","split","snap","mv-view","source","timeline","live-speed","smart-insert","appnd","ripl-owr","close-up","place-on-top","src-owr","set-poi","go-to-poi","2sec","3sec","4sec","5sec","6sec","7sec","in","out","all-cams","cams-9-16","title1","title2","title3","title4","title5","title6","trim-in","trim-out","roll","slip","slide","trans-dur","cam1","cam2","cam3","cam4","cam5","cam6","cam7","cam8","cut","dis","trans","stop-play"];
export const buttonMap = {};
for (let i = 0; i < buttons.length; i++) buttonMap[buttons[i]] = i + 10;

export function applyShuttleDeadzone(raw, deadzone) {
    if (deadzone <= 0 || deadzone >= 0.5) return raw;
    const abs = Math.abs(raw);
    if (abs <= deadzone) return 0;
    return Math.sign(raw) * ((abs - deadzone) / (1 - deadzone));
}

export function createWheelController(options = {}) {
    const emit = options.emit || ((msg) => console.log(msg));
    const drawLed = options.drawLed || (() => {});
    const setTbarLeds = options.setTbarLeds || (() => {});
    const onDisconnect = options.onDisconnect || (() => {});
    const getConfig = options.getConfig || (() => DEFAULT_CFG);
    const nowFn = options.nowFn || (() => performance.now());

    let mode = "jog";
    let previousMode = "jog";
    let modeBeforeFirstScrlClick = "jog";
    let lastScrlClickTime = 0;
    let swallowStopPlayUp = false;

    let remainder = 0;
    let lastWheelTime = 0;
    let lastDir = 0;
    let jogVelocityEma = 0;

    let shuttleRaw = 0;
    let lastSentShuttle = 0;

    let shtlBlinkOn = false;
    let shtlBlinkTimer = null;
    let flushTimer = null;

    // Replay Editor jog LEDs are mapped on HID Output Report 0x04 via main.js surface.draw()
    // (slow-jog = bit 3, scrl-jog = bit 2, jog-jog = bit 0). Output Report 0x03 configures wheel hardware mode.
    function updateModeLeds() {
        drawLed("slow-jog", mode === "slow");
        drawLed("jog-jog", mode === "jog");
        drawLed("scrl-jog", mode === "scrl" || (mode === "shtl" && shtlBlinkOn));
    }

    function stopShtlBlink() {
        if (shtlBlinkTimer) {
            clearInterval(shtlBlinkTimer);
            shtlBlinkTimer = null;
        }
        shtlBlinkOn = false;
    }

    function startShtlBlink() {
        stopShtlBlink();
        shtlBlinkOn = true;
        updateModeLeds();
        shtlBlinkTimer = setInterval(() => {
            if (mode !== "shtl") {
                stopShtlBlink();
                return;
            }
            shtlBlinkOn = !shtlBlinkOn;
            updateModeLeds();
        }, 250);
    }

    function resetWheelState() {
        remainder = 0;
        lastWheelTime = 0;
        lastDir = 0;
        jogVelocityEma = 0;
    }

    function setNormalMode(targetMode) {
        stopShtlBlink();
        mode = targetMode;
        resetWheelState();
        updateModeLeds();
    }

    function enterShuttle() {
        previousMode = modeBeforeFirstScrlClick;
        mode = "shtl";
        resetWheelState();
        shuttleRaw = 0;
        lastSentShuttle = 0;
        emit("SHUTTLE|0");
        startShtlBlink();
    }

    function exitShuttle(targetMode) {
        stopShtlBlink();
        shuttleRaw = 0;
        lastSentShuttle = 0;
        emit("SHUTTLE|0");
        mode = targetMode;
        resetWheelState();
        updateModeLeds();
    }

    function getStepSize(cfg) {
        // Rychlost 10 = prah 1400 (presne jako puvodni SLOW)
        if (mode === "slow") return 14000 / cfg.slowSpeed;
        if (mode === "jog") return 14000 / cfg.jogSpeed;
        return 14000 / cfg.scrlSpeed;
    }

    function getJogVelocityMultiplier(cfg, val, dtMs, dirChanged, wasIdle) {
        const ticks = Math.abs(val) / ENCODER_UNIT;
        const clampedDtSec = Math.max(4, dtMs) / 1000;
        const instantVel = ticks / clampedDtSec;

        if (wasIdle || dirChanged) {
            jogVelocityEma = instantVel;
        } else {
            const alpha = instantVel < jogVelocityEma ? 0.55 : 0.4;
            jogVelocityEma = (jogVelocityEma * (1 - alpha)) + (instantVel * alpha);
        }

        if (!cfg.jogCurveEnabled || cfg.jogCurveRate <= 0) return 1;
        if (jogVelocityEma <= cfg.jogCurveThreshold) return 1;

        const arg = Math.min(50, cfg.jogCurveRate * (jogVelocityEma - cfg.jogCurveThreshold));
        return Math.min(cfg.jogCurveMaxMultiplier, Math.max(1, Math.exp(arg)));
    }

    function handleWheelDelta(val) {
        if (!Number.isFinite(val) || val === 0) return;
        const cfg = getConfig();
        const now = nowFn();

        if (mode === "shtl") {
            lastWheelTime = now;
            const fullScaleRaw = cfg.shuttleSensitivity * ENCODER_UNIT;
            shuttleRaw = Math.max(-1, Math.min(1, shuttleRaw + (val / fullScaleRaw)));
            const outVal = applyShuttleDeadzone(shuttleRaw, cfg.shuttleDeadzone);
            const rounded = Math.round(outVal * 10000) / 10000;
            if (rounded !== lastSentShuttle) {
                lastSentShuttle = rounded;
                emit("SHUTTLE|" + rounded);
            }
            return;
        }

        const dtMs = lastWheelTime > 0 ? (now - lastWheelTime) : 1000;
        const wasIdle = lastWheelTime === 0 || dtMs > 150;
        const dir = val > 0 ? 1 : -1;
        const dirChanged = lastDir !== 0 && dir !== lastDir;
        lastWheelTime = now;
        lastDir = dir;

        // Pri zmene smeru vynulujeme zbytek pro okamzitou odezvu; pri pomalem toceni zbytek zachovame
        if (dirChanged || (val > 0 && remainder < 0) || (val < 0 && remainder > 0)) {
            remainder = 0;
        }

        const stepSize = getStepSize(cfg);
        const multiplier = mode === "jog"
            ? getJogVelocityMultiplier(cfg, val, dtMs, dirChanged, wasIdle)
            : 1;

        remainder += (val / stepSize) * multiplier;
    }

    function flushWheel() {
        if (mode === "shtl") return;
        const eps = 1e-9;
        let steps = Math.trunc(remainder + Math.sign(remainder) * eps);
        if (steps !== 0) {
            steps = Math.max(-500, Math.min(500, steps));
            remainder -= steps;
            if (Math.abs(remainder) < eps) remainder = 0;
            emit("WHEEL|" + steps);
        }
    }

    const hostCallbacks = {
        disconnect: () => {
            dispose();
            onDisconnect();
        },
        keyDownById: (id) => {
            if (id === "slow-jog") {
                lastScrlClickTime = 0;
                if (mode === "shtl") exitShuttle("slow");
                else setNormalMode("slow");
                return;
            }
            if (id === "jog-jog") {
                lastScrlClickTime = 0;
                if (mode === "shtl") exitShuttle("jog");
                else setNormalMode("jog");
                return;
            }
            if (id === "scrl-jog") {
                const now = nowFn();
                if (mode === "shtl") {
                    lastScrlClickTime = 0;
                    exitShuttle("scrl");
                    return;
                }
                const cfg = getConfig();
                if (lastScrlClickTime > 0 && (now - lastScrlClickTime) <= cfg.shuttleDoubleClickMs) {
                    lastScrlClickTime = 0;
                    enterShuttle();
                    return;
                }
                modeBeforeFirstScrlClick = mode;
                lastScrlClickTime = now;
                setNormalMode("scrl");
                return;
            }

            lastScrlClickTime = 0;
            if (id === "stop-play" && mode === "shtl") {
                swallowStopPlayUp = true;
                exitShuttle(previousMode);
                return;
            }

            drawLed(id, true);
            const note = buttonMap[id];
            if (note !== undefined) emit("ON|" + note + "|" + id);
        },
        keyUpById: (id) => {
            if (id === "slow-jog" || id === "jog-jog" || id === "scrl-jog") return;
            if (id === "stop-play" && swallowStopPlayUp) {
                swallowStopPlayUp = false;
                return;
            }
            drawLed(id, false);
            const note = buttonMap[id];
            if (note !== undefined) emit("OFF|" + note);
        },
        sendVariableValue: (name, val) => {
            if (name === "tbarValueVariable") {
                const midiVal = Math.max(0, Math.min(127, Math.round(val * 127)));
                emit("CC|1|" + midiVal);
                setTbarLeds(Math.round(val * 16));
            } else if (name === "jogVelocityVariable") {
                handleWheelDelta(val);
            }
        }
    };

    function start() {
        if (!flushTimer) {
            flushTimer = setInterval(flushWheel, 15);
        }
    }

    function dispose() {
        stopShtlBlink();
        if (flushTimer) {
            clearInterval(flushTimer);
            flushTimer = null;
        }
    }

    return {
        hostCallbacks,
        updateModeLeds,
        flushWheel,
        start,
        dispose,
        getState: () => ({
            mode,
            previousMode,
            remainder,
            jogVelocityEma,
            shuttleRaw,
            lastSentShuttle,
            shtlBlinkOn
        })
    };
}

function isEntrypoint() {
    if (!process.argv[1]) return false;
    try {
        const argPath = fs.realpathSync(process.argv[1]);
        const selfPath = fs.realpathSync(fileURLToPath(import.meta.url));
        return process.platform === "win32"
            ? argPath.toLowerCase() === selfPath.toLowerCase()
            : argPath === selfPath;
    } catch {
        return false;
    }
}

async function main() {
    let cfg = loadMergedConfig();
    const reloadConfig = () => { cfg = loadMergedConfig(); };
    setInterval(reloadConfig, 500);

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
    const controller = createWheelController({
        emit: (msg) => console.log(msg),
        drawLed: (controlId, on) => {
            if (surfaceInstance) {
                surfaceInstance.draw(0, { controlId, color: on ? "#ffffff" : "#000000" });
            }
        },
        setTbarLeds: (count) => {
            if (surfaceInstance) surfaceInstance.onVariableValue("tbarLeds", count);
        },
        onDisconnect: () => {
            console.log("STATUS|DISCONNECTED");
            process.exit(3);
        },
        getConfig: () => cfg
    });

    controller.start();

    let openedOk = false;
    for (const cand of candidates) {
        try {
            const opened = await plugin.openSurface("replay", { path: cand.path }, controller.hostCallbacks);
            surfaceInstance = opened.surface;
            await surfaceInstance.init();
            controller.updateModeLeds();
            openedOk = true;
            console.log("STATUS|READY");
            break;
        } catch (e) {}
    }

    if (!openedOk) {
        controller.dispose();
        console.log("STATUS|BUSY");
        process.exit(4);
    }

    process.on("SIGINT", () => { controller.dispose(); process.exit(0); });
    process.on("SIGTERM", () => { controller.dispose(); process.exit(0); });
    setInterval(() => {}, 60000);
}

if (isEntrypoint()) {
    await main();
}
