export type Point = { x: number; y: number }
export type Rect = { x: number; y: number; width: number; height: number }
export type Tool = "select" | "pen" | "rect" | "ellipse" | "line" | "arrow"
export type DrawingTool = Exclude<Tool, "select">
export type Mark = {
  tool: DrawingTool
  color: string
  start: Point
  end: Point
  points: Point[]
}

export function rectangleBetween(a: Point, b: Point): Rect {
  return { x: Math.min(a.x, b.x), y: Math.min(a.y, b.y), width: Math.abs(a.x - b.x), height: Math.abs(a.y - b.y) }
}

export function contains(rect: Rect, point: Point): boolean {
  return point.x >= rect.x && point.x <= rect.x + rect.width && point.y >= rect.y && point.y <= rect.y + rect.height
}

export function clampToRect(rect: Rect, point: Point): Point {
  return {
    x: Math.max(rect.x, Math.min(rect.x + rect.width, point.x)),
    y: Math.max(rect.y, Math.min(rect.y + rect.height, point.y)),
  }
}

export function paintMark(ctx: CanvasRenderingContext2D, mark: Mark): void {
  ctx.save()
  ctx.strokeStyle = mark.color
  ctx.lineWidth = 3
  ctx.lineCap = "round"
  ctx.lineJoin = "round"
  const box = rectangleBetween(mark.start, mark.end)

  if (mark.tool === "pen") {
    if (mark.points.length > 1) {
      ctx.beginPath()
      ctx.moveTo(mark.points[0].x, mark.points[0].y)
      for (const point of mark.points.slice(1)) ctx.lineTo(point.x, point.y)
      ctx.stroke()
    }
  } else if (mark.tool === "rect") {
    ctx.strokeRect(box.x, box.y, box.width, box.height)
  } else if (mark.tool === "ellipse") {
    ctx.beginPath()
    ctx.ellipse(box.x + box.width / 2, box.y + box.height / 2, Math.max(0.5, box.width / 2), Math.max(0.5, box.height / 2), 0, 0, Math.PI * 2)
    ctx.stroke()
  } else {
    ctx.beginPath()
    ctx.moveTo(mark.start.x, mark.start.y)
    ctx.lineTo(mark.end.x, mark.end.y)
    ctx.stroke()
    if (mark.tool === "arrow") {
      const angle = Math.atan2(mark.end.y - mark.start.y, mark.end.x - mark.start.x)
      const length = Math.min(17, Math.max(9, Math.hypot(mark.end.x - mark.start.x, mark.end.y - mark.start.y) * 0.25))
      ctx.beginPath()
      ctx.moveTo(mark.end.x - length * Math.cos(angle - Math.PI / 6), mark.end.y - length * Math.sin(angle - Math.PI / 6))
      ctx.lineTo(mark.end.x, mark.end.y)
      ctx.lineTo(mark.end.x - length * Math.cos(angle + Math.PI / 6), mark.end.y - length * Math.sin(angle + Math.PI / 6))
      ctx.stroke()
    }
  }
  ctx.restore()
}
