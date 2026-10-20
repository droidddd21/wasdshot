import assert from "node:assert/strict"
import { readFileSync } from "node:fs"
import { resolve } from "node:path"
import { test } from "node:test"

const bundle = readFileSync(resolve("dist/app.js"), "utf8")

for (const color of ["", "-red", "-green"]) {
  test(`Pet ${color || "-blue"} is embedded as a canvas-safe data URL`, () => {
    const png = readFileSync(resolve(`../assets/axolotl-pet${color}.png`))
    const dataUrl = `data:image/png;base64,${png.toString("base64")}`
    assert.ok(bundle.includes(dataUrl), `Missing embedded Pet ${color || "-blue"}`)
  })
}
