// T-086: colour values live only in app/globals.css. Fails when a hex, rgb() or hsl() colour appears anywhere else
// in app/, components/ or lib/, so components can only use the design tokens.
import { readdirSync, readFileSync, statSync } from "node:fs";
import { join, relative } from "node:path";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("..", import.meta.url));
const allowed = new Set(["app/globals.css"]);
const pattern = /#[0-9a-fA-F]{3,8}\b|\brgba?\(|\bhsla?\(|\boklch\(/;
const failures = [];

function walk(directory) {
  for (const name of readdirSync(directory)) {
    const path = join(directory, name);
    if (statSync(path).isDirectory()) {
      walk(path);
      continue;
    }

    const file = relative(root, path);
    if (!/\.(tsx?|css)$/.test(name) || allowed.has(file)) {
      continue;
    }

    readFileSync(path, "utf8").split("\n").forEach((line, index) => {
      // Skip comments and JSX entities such as &#39;.
      const code = line.replace(/\/\/.*$/, "").replace(/&#\d+;/g, "");
      if (pattern.test(code)) {
        failures.push(`${file}:${index + 1}: ${line.trim()}`);
      }
    });
  }
}

for (const directory of ["app", "components", "lib"]) {
  walk(join(root, directory));
}

if (failures.length > 0) {
  console.error("Raw colour values outside app/globals.css (use a token):\n" + failures.join("\n"));
  process.exit(1);
}

console.log("No raw colour values outside app/globals.css.");
