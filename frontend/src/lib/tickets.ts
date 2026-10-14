import { useState, type ClipboardEvent } from 'react'
import type { TicketStatus, TicketType } from '../api/types'

export const MAX_TICKET_IMAGES = 4
const MAX_BYTES = 5 * 1024 * 1024 // 5 MB por imagem (a Api também valida)

export const STATUS_META: Record<TicketStatus, { label: string; cls: string }> = {
  Open: { label: 'Aberto', cls: 'st-open' },
  InReview: { label: 'Em análise', cls: 'st-review' },
  InDevelopment: { label: 'Em desenvolvimento', cls: 'st-dev' },
  Completed: { label: 'Concluído', cls: 'st-done' },
  Cancelled: { label: 'Cancelado', cls: 'st-cancel' },
}

export const STATUS_ORDER = Object.keys(STATUS_META) as TicketStatus[]

export const TYPE_META: Record<TicketType, { label: string }> = {
  Bug: { label: 'Bug' },
  Idea: { label: 'Sugestão' },
}

/** Chamado concluído/cancelado não se edita mais — para isso, abre-se um novo. */
export const isClosed = (status: TicketStatus) => status === 'Completed' || status === 'Cancelled'

/** Print anexado a um chamado: ou já está no servidor (`url`), ou é novo (`dataUrl`). */
export interface ImageAttachment {
  id: string
  /** Anexo já salvo ("/api/uploads/tickets/..."), vindo de um chamado existente. */
  url?: string
  /** Anexo novo, ainda como data URL (arquivo escolhido ou print colado). */
  dataUrl?: string
}

function readFile(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(reader.result as string)
    reader.onerror = () => reject(reader.error)
    reader.readAsDataURL(file)
  })
}

/**
 * Estado dos prints de um chamado: anexar por arquivo, colar (Ctrl+V) e remover. Usado no envio (botão de
 * feedback) e na edição (Meus chamados), onde começa com os anexos que já existem.
 */
export function useImageAttachments(initialUrls: string[] = []) {
  const [images, setImages] = useState<ImageAttachment[]>(() => initialUrls.map((url) => ({ id: url, url })))
  const [error, setError] = useState<string | null>(null)

  async function addFiles(files: File[]) {
    setError(null)
    const imageFiles = files.filter((f) => f.type.startsWith('image/'))
    if (imageFiles.length === 0) return

    const room = MAX_TICKET_IMAGES - images.length
    if (room <= 0) {
      setError(`Você pode anexar no máximo ${MAX_TICKET_IMAGES} imagens.`)
      return
    }

    const accepted: ImageAttachment[] = []
    for (const file of imageFiles) {
      if (accepted.length >= room) {
        setError(`Você pode anexar no máximo ${MAX_TICKET_IMAGES} imagens.`)
        break
      }
      if (file.size > MAX_BYTES) {
        setError(`Cada imagem deve ter no máximo ${MAX_BYTES / (1024 * 1024)} MB.`)
        continue
      }
      accepted.push({ id: crypto.randomUUID(), dataUrl: await readFile(file) })
    }

    if (accepted.length > 0) setImages((prev) => [...prev, ...accepted].slice(0, MAX_TICKET_IMAGES))
  }

  function onPaste(e: ClipboardEvent) {
    const files = Array.from(e.clipboardData.items)
      .filter((it) => it.kind === 'file' && it.type.startsWith('image/'))
      .map((it) => it.getAsFile())
      .filter((f): f is File => f !== null)
    if (files.length > 0) {
      e.preventDefault()
      void addFiles(files)
    }
  }

  return {
    images,
    error,
    addFiles,
    onPaste,
    remove: (id: string) => setImages((prev) => prev.filter((i) => i.id !== id)),
    reset: () => {
      setImages([])
      setError(null)
    },
    /** URLs dos anexos que já existiam e continuam. */
    keptUrls: () => images.flatMap((i) => (i.url ? [i.url] : [])),
    /** Data URLs dos anexos novos. */
    newDataUrls: () => images.flatMap((i) => (i.dataUrl ? [i.dataUrl] : [])),
  }
}
