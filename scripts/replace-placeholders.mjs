#!/usr/bin/env node
/**
 * Post-process compiled JS to restore FileTransformation placeholders and
 * strip ES-module syntax that Typed Sources add but the runtime expects
 * as plain script.
 *
 * - Sentinels (__LAYOUTMANAGER_HOOK__ etc.) → {{...}} for TransformationPatches.cs
 * - `export {}` / `export function` → plain script (jellyfin-web loads them as
 *   classic <script> or string-injected chunk, not ES modules)
 */
import { readFile, writeFile } from "node:fs/promises";
import { existsSync } from "node:fs";

const targets = [
    "src/Jellyfin.Plugin.HomeScreenSections/Inject/HomeScreenSections.js",
    "src/Jellyfin.Plugin.HomeScreenSections/Controllers/loadSections.js",
];

const replacements = [
    ['"__LAYOUTMANAGER_HOOK__"', "{{layoutmanager_hook}}"],
    ["'__LAYOUTMANAGER_HOOK__'", "{{layoutmanager_hook}}"],
    ["__LAYOUTMANAGER_HOOK__", "{{layoutmanager_hook}}"],

    ['"__CARDBUILDER_HOOK__"', "{{cardbuilder_hook}}"],
    ["'__CARDBUILDER_HOOK__'", "{{cardbuilder_hook}}"],
    ["__CARDBUILDER_HOOK__", "{{cardbuilder_hook}}"],

    ['"__THIS_HOOK__"', "{{this_hook}}"],
    ["'__THIS_HOOK__'", "{{this_hook}}"],
    ["__THIS_HOOK__", "{{this_hook}}"],
];

let changed = 0;
for (const p of targets) {
    if (!existsSync(p)) {
        console.warn(`[replace-placeholders] skip missing ${p} (run tsc first)`);
        continue;
    }
    let text = await readFile(p, "utf8");
    const before = text;
    for (const [from, to] of replacements) {
        text = text.split(from).join(to);
    }
    // Strip ES-module artifacts emitted for type-only sources:
    // HomeScreenSections.ts emits `export {};` / loadSections.ts emits `export function test`
    // Both break when loaded as classic script / chunk-injected function.
    text = text.replace(/^\s*export\s+\{\s*\}\s*;?\s*$/m, "");
    text = text.replace(/^\s*export\s+function\s+test/m, "function test");
    // Also handle `export function` if tsc ever mangles spacing
    text = text.replace(/\bexport\s+function\s+test\b/, "function test");

    if (text !== before) {
        await writeFile(p, text, "utf8");
        console.log(`[replace-placeholders] patched ${p}`);
        changed++;
    } else {
        // Still check if export-stripping changed it (before comparison already includes it)
        // The check above already covers it; no extra log needed.
    }
}

if (changed === 0) {
    console.log("[replace-placeholders] no changes (already clean or nothing to patch)");
}
