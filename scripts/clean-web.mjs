#!/usr/bin/env node
import { rm } from "node:fs/promises";
const outs = [
    "src/Jellyfin.Plugin.HomeScreenSections/Inject/HomeScreenSections.js",
    "src/Jellyfin.Plugin.HomeScreenSections/Controllers/loadSections.js",
];
for (const p of outs) {
    await rm(p, { force: true });
    console.log(`[clean-web] removed ${p}`);
}
