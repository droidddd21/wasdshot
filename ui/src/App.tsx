import { useEffect, useRef, useState, type PointerEvent, type ReactNode } from "react"
import { ArrowUpRight, Check, Circle, Clipboard, Crop, Minus, Palette, PenLine, RectangleHorizontal, Save, Undo2, X } from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardAction, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from "@/components/ui/card"
import { Separator } from "@/components/ui/separator"
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group"
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip"
import { clampToRect, contains, paintMark, rectangleBetween, type DrawingTool, type Mark, type Point, type Rect, type Tool } from "./drawing"
import { placePet } from "./pet"
import bluePetUrl from "../../assets/axolotl-pet.png"
import redPetUrl from "../../assets/axolotl-pet-red.png"
import greenPetUrl from "../../assets/axolotl-pet-green.png"
import ivavaPetUrl from "../../assets/ivava-pet.png"
import mikuPetUrl from "../../assets/miku-pet.png"

type Bridge = { postMessage: (message: string) => void; addEventListener: (name: string, listener: (event: { data: string }) => void) => void }
declare global { interface Window { chrome?: { webview?: Bridge } } }
type Language = "ru" | "en"
type PetKind = "axolotl" | "ivava" | "miku"

const inks = [
  { id: "ice", label: "Лёд", value: "#8dd9ff" },
  { id: "blue", label: "Голубой", value: "#4f9dff" },
  { id: "lilac", label: "Сирень", value: "#b4a0ff" },
  { id: "coral", label: "Коралл", value: "#ff9cad" },
]
const themes = [
  { id: "blue", label: "Голубая тема", value: "#70c7ff" },
  { id: "red", label: "Красная тема", value: "#ff8d92" },
  { id: "green", label: "Зелёная тема", value: "#85d6a5" },
]
const petUrls: Record<string, string> = { blue: bluePetUrl, red: redPetUrl, green: greenPetUrl }
const petKinds: { id: PetKind; label: string; url: string }[] = [
  { id: "axolotl", label: "Acelot", url: bluePetUrl },
  { id: "ivava", label: "Iwawwa", url: ivavaPetUrl },
  { id: "miku", label: "Miku", url: mikuPetUrl },
]
const send = (message: string) => window.chrome?.webview?.postMessage(message)

function IconAction({ label, disabled, onClick, children }: { label: string; disabled?: boolean; onClick: () => void; children: ReactNode }) {
  return <Tooltip><TooltipTrigger render={<Button size="icon-sm" variant="ghost" aria-label={label} disabled={disabled} onClick={onClick} />}>{children}</TooltipTrigger><TooltipContent>{label}</TooltipContent></Tooltip>
}

export default function App() {
  const screenshotElement = useRef<HTMLImageElement>(null)
  const annotationCanvas = useRef<HTMLCanvasElement>(null)
  const screenshot = useRef<HTMLImageElement | null>(null)
  const selectionRef = useRef<Rect | null>(null)
  const marksRef = useRef<Mark[]>([])
  const currentMark = useRef<Mark | null>(null)
  const dragStart = useRef<Point | null>(null)
  const animationFrame = useRef<number | null>(null)
  const selectionFrame = useRef<number | null>(null)
  const pendingSelection = useRef<Rect | null>(null)
  const petImages = useRef<Record<string, HTMLImageElement>>({})
  const copyFromHost = useRef<() => void>(() => {})
  const undoFromHost = useRef<() => void>(() => {})
  const petDrag = useRef<Point | null>(null)
  const petEnabledRef = useRef(false)
  const petKindRef = useRef<PetKind>("axolotl")
  const petPositionRef = useRef<Point | null>(null)
  const [loaded, setLoaded] = useState(false)
  const [shortcut, setShortcut] = useState("Win + Shift + S")
  const [selection, setSelection] = useState<Rect | null>(null)
  const [draftSelection, setDraftSelection] = useState<Rect | null>(null)
  const [tool, setTool] = useState<Tool>("select")
  const [ink, setInk] = useState(inks[0].id)
  const [theme, setTheme] = useState("blue")
  const [markCount, setMarkCount] = useState(0)
  const [, setPetAssetsVersion] = useState(0)
  const [petEnabled, setPetEnabled] = useState(false)
  const [petPosition, setPetPosition] = useState<Point | null>(null)
  const [petKind, setPetKind] = useState<PetKind>("axolotl")
  const [quickSave, setQuickSave] = useState(false)
  const [language, setLanguage] = useState<Language>(() => {
    try { return localStorage.getItem("wasdshot-language") === "en" ? "en" : "ru" } catch { return "ru" }
  })
  const labels = language === "en" ? {
    selectArea: "Select an area", exit: "Esc — exit", tools: "Tools and marks", select: "New selection",
    pen: "Pen", rectangle: "Rectangle", ellipse: "Ellipse", line: "Line", arrow: "Arrow",
    undo: "Undo", newSelection: "New screenshot", copy: "Copy", save: "Save", pet: "Pet",
    showPet: "Show or hide pet", interfaceColor: "Interface and pet color", inkColor: "Ink color",
    close: "Close", language: "Language", copiedSize: "Screenshot"
  } : {
    selectArea: "Выделите область", exit: "Esc — выход", tools: "Фигуры и пометки", select: "Новое выделение",
    pen: "Перо", rectangle: "Прямоугольник", ellipse: "Эллипс", line: "Линия", arrow: "Стрелка",
    undo: "Отменить", newSelection: "Новый скриншот", copy: "Копировать", save: "Сохранить", pet: "Pet",
    showPet: "Показать или скрыть ацелота", interfaceColor: "Цвет интерфейса и ацелота", inkColor: "Цвет линии",
    close: "Закрыть", language: "Язык", copiedSize: "Снимок"
  }

  useEffect(() => {
    for (const choice of themes) {
      const image = new Image()
      image.onload = () => { petImages.current[choice.id] = image; setPetAssetsVersion(version => version + 1) }
      image.src = petUrls[choice.id]
    }
    for (const pet of petKinds.slice(1)) {
      const image = new Image()
      image.onload = () => { petImages.current[pet.id] = image; setPetAssetsVersion(version => version + 1) }
      image.src = pet.url
    }
  }, [])

  const repaintAnnotations = () => {
    animationFrame.current = null
    const canvas = annotationCanvas.current, region = selectionRef.current
    if (!canvas) return
    if (!region) { canvas.style.display = "none"; return }
    const ratio = window.devicePixelRatio || 1
    const width = Math.max(1, Math.round(region.width * ratio))
    const height = Math.max(1, Math.round(region.height * ratio))
    if (canvas.width !== width) canvas.width = width
    if (canvas.height !== height) canvas.height = height
    canvas.style.display = "block"
    canvas.style.left = `${region.x}px`
    canvas.style.top = `${region.y}px`
    canvas.style.width = `${region.width}px`
    canvas.style.height = `${region.height}px`
    const ctx = canvas.getContext("2d")
    if (!ctx) return
    ctx.setTransform(ratio, 0, 0, ratio, 0, 0)
    ctx.clearRect(0, 0, region.width, region.height)
    ctx.save()
    ctx.translate(-region.x, -region.y)
    for (const mark of marksRef.current) paintMark(ctx, mark)
    if (currentMark.current) paintMark(ctx, currentMark.current)
    ctx.restore()
  }
  const schedulePaint = () => {
    if (animationFrame.current === null) animationFrame.current = requestAnimationFrame(repaintAnnotations)
  }

  useEffect(() => {
    const bridge = window.chrome?.webview
    if (!bridge) return
    let captureRequest = 0
    bridge.addEventListener("message", event => {
      if (typeof event.data !== "string") return
      if (event.data.startsWith("capture:")) {
        const request = ++captureRequest
        screenshot.current = null
        if (screenshotElement.current) screenshotElement.current.removeAttribute("src")
        selectionRef.current = null
        marksRef.current = []
        currentMark.current = null
        dragStart.current = null
        setSelection(null)
        setDraftSelection(null)
        setMarkCount(0)
        setTool("select")
        petPositionRef.current = null
        setPetPosition(null)
        setLoaded(false)
        if (annotationCanvas.current) annotationCanvas.current.style.display = "none"
        const image = new Image()
        image.onload = () => {
          if (request !== captureRequest) return
          screenshot.current = image
          if (screenshotElement.current) screenshotElement.current.src = image.src
          setLoaded(true)
          requestAnimationFrame(() => requestAnimationFrame(() => {
            if (request === captureRequest && screenshotElement.current?.complete) send("loaded")
          }))
        }
        image.src = `data:image/png;base64,${event.data.slice(8)}`
      } else if (event.data === "cancelled") {
        captureRequest++
        screenshot.current = null
        if (screenshotElement.current) screenshotElement.current.removeAttribute("src")
        setLoaded(false)
      } else if (event.data === "copy-request") {
        copyFromHost.current()
      } else if (event.data === "undo-request") {
        undoFromHost.current()
      } else if (event.data === "pet:on" || event.data === "pet:off") {
        const enabled = event.data === "pet:on"
        petEnabledRef.current = enabled
        setPetEnabled(enabled)
      } else if (event.data.startsWith("pet-kind:")) {
        const next = event.data.slice(9)
        if (next === "axolotl" || next === "ivava" || next === "miku") { petKindRef.current = next; setPetKind(next) }
      } else if (event.data === "quick-save:on" || event.data === "quick-save:off") {
        setQuickSave(event.data === "quick-save:on")
      } else if (event.data.startsWith("language:")) {
        chooseLanguage(event.data.slice(9))
      } else if (event.data.startsWith("shortcut:")) setShortcut(event.data.slice(9))
      else if (event.data.startsWith("theme:")) {
        const next = event.data.slice(6)
        if (themes.some(choice => choice.id === next)) {
          setTheme(next)
          document.documentElement.dataset.theme = next
        }
      }
    })
    send("ready")
    return () => {
      if (animationFrame.current !== null) cancelAnimationFrame(animationFrame.current)
      if (selectionFrame.current !== null) cancelAnimationFrame(selectionFrame.current)
    }
  }, [])

  const scheduleDraftSelection = (region: Rect) => {
    pendingSelection.current = region
    if (selectionFrame.current !== null) return
    selectionFrame.current = requestAnimationFrame(() => {
      selectionFrame.current = null
      setDraftSelection(pendingSelection.current)
    })
  }

  const stopDraftAnimation = () => {
    if (selectionFrame.current !== null) cancelAnimationFrame(selectionFrame.current)
    selectionFrame.current = null
    pendingSelection.current = null
  }

  const clearSelection = () => {
    selectionRef.current = null
    marksRef.current = []
    currentMark.current = null
    dragStart.current = null
    setSelection(null)
    setDraftSelection(null)
    setMarkCount(0)
    setTool("select")
    setPetPosition(null)
    schedulePaint()
  }

  const chooseLanguage = (next: string) => {
    if (next !== "ru" && next !== "en") return
    setLanguage(next)
    try { localStorage.setItem("wasdshot-language", next) } catch { }
  }

  const exportImage = (action: "copy" | "save" | "quick-save") => {
    try {
      const image = screenshot.current, region = selectionRef.current
      if (!image || !region || region.width < 6 || region.height < 6) return
      const scaleX = image.naturalWidth / window.innerWidth
      const scaleY = image.naturalHeight / window.innerHeight
      const canvas = document.createElement("canvas")
      canvas.width = Math.max(1, Math.round(region.width * scaleX))
      canvas.height = Math.max(1, Math.round(region.height * scaleY))
      const ctx = canvas.getContext("2d")
      if (!ctx) throw new Error("Не удалось создать canvas для снимка")
      ctx.drawImage(image, region.x * scaleX, region.y * scaleY, region.width * scaleX, region.height * scaleY, 0, 0, canvas.width, canvas.height)
      ctx.save()
      ctx.scale(scaleX, scaleY)
      ctx.translate(-region.x, -region.y)
      for (const mark of marksRef.current) paintMark(ctx, mark)
      const exportPet = petImages.current[petKindRef.current] || petImages.current[theme]
      const exportPetRect = petEnabledRef.current && exportPet ? placePet(region, exportPet.naturalWidth / exportPet.naturalHeight, petPositionRef.current) : null
      if (exportPet && exportPetRect) ctx.drawImage(exportPet, exportPetRect.x, exportPetRect.y, exportPetRect.width, exportPetRect.height)
      ctx.restore()
      send(`${action}:${canvas.toDataURL("image/png").split(",")[1]}`)
    } catch (error) {
      send(`error:${error instanceof Error ? error.message : String(error)}`)
    }
  }
  copyFromHost.current = () => exportImage("copy")
  undoFromHost.current = () => {
    if (!marksRef.current.length) return
    marksRef.current = marksRef.current.slice(0, -1)
    setMarkCount(marksRef.current.length)
    schedulePaint()
  }

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") { send("cancel"); return }
      if (!selectionRef.current) return
      const key = event.key.toLowerCase()
      if (event.key === "Enter" || (event.ctrlKey && key === "c")) { event.preventDefault(); exportImage("copy") }
      else if (event.ctrlKey && key === "s") { event.preventDefault(); exportImage("save") }
      else if (event.ctrlKey && key === "z") {
        event.preventDefault()
        undoFromHost.current()
      }
    }
    window.addEventListener("keydown", onKey)
    return () => window.removeEventListener("keydown", onKey)
  })

  const pointerDown = (event: PointerEvent<HTMLDivElement>) => {
    if (event.button !== 0 || !loaded) return
    const point = { x: event.clientX, y: event.clientY }
    const region = selectionRef.current
    if (region && tool !== "select" && contains(region, point)) {
      const color = inks.find(choice => choice.id === ink)!.value
      currentMark.current = { tool: tool as DrawingTool, color, start: point, end: point, points: [point] }
      schedulePaint()
    } else {
      dragStart.current = point
      selectionRef.current = null
      marksRef.current = []
      setMarkCount(0)
      setSelection(null)
      setDraftSelection({ x: point.x, y: point.y, width: 0, height: 0 })
      setTool("pen")
      petPositionRef.current = null
      setPetPosition(null)
      schedulePaint()
    }
    event.currentTarget.setPointerCapture(event.pointerId)
  }
  const pointerMove = (event: PointerEvent<HTMLDivElement>) => {
    const point = { x: event.clientX, y: event.clientY }
    if (dragStart.current) scheduleDraftSelection(rectangleBetween(dragStart.current, point))
    if (currentMark.current && selectionRef.current) {
      const next = clampToRect(selectionRef.current, point)
      currentMark.current.end = next
      if (currentMark.current.tool === "pen") currentMark.current.points.push(next)
      schedulePaint()
    }
  }
  const pointerUp = (event: PointerEvent<HTMLDivElement>) => {
    if (dragStart.current) {
      stopDraftAnimation()
      const box = rectangleBetween(dragStart.current, { x: event.clientX, y: event.clientY })
      const region = box.width >= 6 && box.height >= 6 ? box : null
      selectionRef.current = region
      setSelection(region)
      petPositionRef.current = null
      setPetPosition(null)
      setDraftSelection(null)
      dragStart.current = null
      setTool("pen")
      if (region && quickSave) requestAnimationFrame(() => exportImage("quick-save"))
      schedulePaint()
    }
    if (currentMark.current) {
      const mark = currentMark.current
      const distance = Math.hypot(mark.end.x - mark.start.x, mark.end.y - mark.start.y)
      if (distance >= 2 || (mark.tool === "pen" && mark.points.length > 1)) {
        marksRef.current = [...marksRef.current, mark]
        setMarkCount(marksRef.current.length)
      }
      currentMark.current = null
      schedulePaint()
    }
    if (event.currentTarget.hasPointerCapture(event.pointerId)) event.currentTarget.releasePointerCapture(event.pointerId)
  }
  const pointerCancel = () => {
    stopDraftAnimation()
    dragStart.current = null
    currentMark.current = null
    setDraftSelection(null)
    schedulePaint()
  }

  const visible = draftSelection ?? selection
  const activePet = petImages.current[petKind] || petImages.current[theme]
  const petRect = selection && petEnabled && activePet ? placePet(selection, activePet.naturalWidth / activePet.naturalHeight, petPosition) : null
  const themePicker = <ToggleGroup value={[theme]} onValueChange={value => {
    const next = value[0]
    if (next) { setTheme(next); document.documentElement.dataset.theme = next; send(`theme:${next}`) }
  }} size="sm" spacing={0} aria-label={labels.interfaceColor}>{themes.map(choice =>
    <ToggleGroupItem key={choice.id} value={choice.id} aria-label={choice.label} title={choice.label}><span className="theme-dot" style={{ backgroundColor: choice.value }}>{theme === choice.id && <Check />}</span></ToggleGroupItem>
  )}</ToggleGroup>
  const languagePicker = <ToggleGroup value={[language]} onValueChange={value => chooseLanguage(value[0])} size="sm" spacing={0} aria-label={labels.language}>
    <ToggleGroupItem value="ru" aria-label="Русский" title="Русский">RU</ToggleGroupItem>
    <ToggleGroupItem value="en" aria-label="English" title="English">EN</ToggleGroupItem>
  </ToggleGroup>
  const viewportWidth = window.innerWidth
  const viewportHeight = window.innerHeight
  const toolbarWidth = 508
  const left = selection ? Math.max(12, Math.min(viewportWidth - toolbarWidth - 12, selection.x + selection.width / 2 - toolbarWidth / 2)) : 0
  const top = selection ? (selection.y + selection.height + 15 + 126 < viewportHeight ? selection.y + selection.height + 15 : Math.max(12, selection.y - 142)) : 0

  return <TooltipProvider><main className="capture-shell" aria-label="wasdshot screenshot overlay">
    <img ref={screenshotElement} className="desktop-capture" alt="" draggable={false} />
    {!visible && <div className="screen-shade screen-shade-full" />}
    {visible && <>
      <div className="selection-frame" style={{ left: visible.x, top: visible.y, width: visible.width, height: visible.height }} />
      <div className="size-badge" style={{ left: visible.x, top: Math.max(8, visible.y - 32) }}><Badge variant="secondary">{Math.round(visible.width)} × {Math.round(visible.height)}</Badge></div>
    </>}
    <canvas ref={annotationCanvas} className="annotation-canvas" aria-hidden="true" />
    <div className="capture-interaction" onPointerDown={pointerDown} onPointerMove={pointerMove} onPointerUp={pointerUp} onPointerCancel={pointerCancel} />
    {petRect && <img className="pet-sticker" src={activePet.src} alt="Перетащить ацелота" draggable={false} style={{ left: petRect.x, top: petRect.y, width: petRect.width, height: petRect.height }}
      onPointerDown={event => {
        event.preventDefault()
        event.stopPropagation()
        petDrag.current = { x: event.clientX - petRect.x, y: event.clientY - petRect.y }
        event.currentTarget.setPointerCapture(event.pointerId)
      }}
      onPointerMove={event => {
        if (!petDrag.current || !selectionRef.current) return
        const next = placePet(selectionRef.current, activePet.naturalWidth / activePet.naturalHeight, { x: event.clientX - petDrag.current.x, y: event.clientY - petDrag.current.y })
        petPositionRef.current = { x: next.x, y: next.y }
        setPetPosition({ x: next.x, y: next.y })
      }}
      onPointerUp={event => { petDrag.current = null; if (event.currentTarget.hasPointerCapture(event.pointerId)) event.currentTarget.releasePointerCapture(event.pointerId) }}
      onPointerCancel={() => { petDrag.current = null }} />}
    {!selection && <div className="start-hint"><Crop aria-hidden="true" /><strong>wasdshot</strong><Separator orientation="vertical" /><span>{labels.selectArea}</span><Badge variant="outline">{shortcut}</Badge><Separator orientation="vertical" /><Palette aria-hidden="true" />{themePicker}{languagePicker}<span>{labels.exit}</span></div>}
    {selection && <Card size="sm" className="action-card" style={{ left, top }}>
      <CardHeader className="action-head"><CardTitle>wasdshot</CardTitle><CardDescription>{labels.tools}</CardDescription><CardAction><Badge variant="secondary">{Math.round(selection.width)} × {Math.round(selection.height)}</Badge></CardAction></CardHeader>
      <CardContent className="action-content">
        <ToggleGroup value={[tool]} onValueChange={value => value[0] && setTool(value[0] as Tool)} size="sm" spacing={1} aria-label={labels.tools}>
          <ToggleGroupItem value="pen" aria-label={labels.pen} title={labels.pen}><PenLine /></ToggleGroupItem>
          <ToggleGroupItem value="rect" aria-label={labels.rectangle} title={labels.rectangle}><RectangleHorizontal /></ToggleGroupItem>
          <ToggleGroupItem value="ellipse" aria-label={labels.ellipse} title={labels.ellipse}><Circle /></ToggleGroupItem>
          <ToggleGroupItem value="line" aria-label={labels.line} title={labels.line}><Minus /></ToggleGroupItem>
          <ToggleGroupItem value="arrow" aria-label={labels.arrow} title={labels.arrow}><ArrowUpRight /></ToggleGroupItem>
        </ToggleGroup>
        <Separator orientation="vertical" />
        <ToggleGroup value={[ink]} onValueChange={value => value[0] && setInk(value[0])} size="sm" spacing={0} aria-label={labels.inkColor}>{inks.map(choice => <ToggleGroupItem key={choice.id} value={choice.id} aria-label={choice.label} title={choice.label}><span className="ink-dot" style={{ backgroundColor: choice.value }}>{ink === choice.id && <Check />}</span></ToggleGroupItem>)}</ToggleGroup>
        <Separator orientation="vertical" />
        <IconAction label={labels.undo} disabled={!markCount} onClick={() => { marksRef.current = marksRef.current.slice(0, -1); setMarkCount(marksRef.current.length); schedulePaint() }}><Undo2 /></IconAction>
        <IconAction label={labels.newSelection} onClick={clearSelection}><Crop /></IconAction>
      </CardContent>
      <CardFooter className="action-footer"><Button size="sm" variant="outline" onClick={() => exportImage("copy")}><Clipboard data-icon="inline-start" /> {labels.copy}</Button><Button size="sm" onClick={() => exportImage("save")}><Save data-icon="inline-start" /> {labels.save}</Button><Separator orientation="vertical" />{themePicker}{languagePicker}<IconAction label={labels.newSelection} onClick={clearSelection}><Crop /></IconAction><IconAction label={labels.close} onClick={() => send("cancel")}><X /></IconAction></CardFooter>
    </Card>}
  </main></TooltipProvider>
}

