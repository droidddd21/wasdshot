import test from "node:test"
import assert from "node:assert/strict"
import { placePet } from "../dist/pet.js"

test("pet starts fully inside the screenshot selection", () => {
  const selection = { x: 100, y: 50, width: 500, height: 300 }
  const pet = placePet(selection, 1.4)
  assert.ok(pet.x >= selection.x && pet.y >= selection.y)
  assert.ok(pet.x + pet.width <= selection.x + selection.width)
  assert.ok(pet.y + pet.height <= selection.y + selection.height)
})

test("dragging cannot move pet outside the captured pixels", () => {
  const selection = { x: 10, y: 20, width: 100, height: 80 }
  const topLeft = placePet(selection, 1.4, { x: -900, y: -900 })
  const bottomRight = placePet(selection, 1.4, { x: 900, y: 900 })
  assert.equal(topLeft.x, 10)
  assert.equal(topLeft.y, 20)
  assert.equal(bottomRight.x + bottomRight.width, 110)
  assert.equal(bottomRight.y + bottomRight.height, 100)
})

test("pet scales down in a small selection", () => {
  const pet = placePet({ x: 0, y: 0, width: 40, height: 30 }, 1.4)
  assert.ok(pet.width <= 40 && pet.height <= 30)
  assert.ok(pet.width > 0 && pet.height > 0)
})
