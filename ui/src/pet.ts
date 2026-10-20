import type { Point, Rect } from "./drawing"

export type PetRect = Rect

export function placePet(selection: Rect, imageRatio: number, position?: Point | null): PetRect {
  const ratio = Number.isFinite(imageRatio) && imageRatio > 0 ? imageRatio : 1
  const width = Math.max(1, Math.min(140, selection.width * 0.45, selection.height * 0.5 * ratio))
  const height = width / ratio
  const margin = Math.min(8, selection.width * 0.05, selection.height * 0.05)
  const defaultX = selection.x + selection.width - width - margin
  const defaultY = selection.y + selection.height - height - margin
  return {
    x: Math.max(selection.x, Math.min(selection.x + selection.width - width, position?.x ?? defaultX)),
    y: Math.max(selection.y, Math.min(selection.y + selection.height - height, position?.y ?? defaultY)),
    width,
    height,
  }
}
