export function Pager({ page, pageSize, total, onPage }: {
  page: number
  pageSize: number
  total: number
  onPage: (p: number) => void
}) {
  const pages = Math.max(1, Math.ceil(total / pageSize))
  if (pages <= 1) return null
  return (
    <nav className="pager" aria-label="Paginação">
      <button type="button" className="btn btn-sm" disabled={page <= 1} onClick={() => onPage(page - 1)}>Anterior</button>
      <span className="mono note">página {page} de {pages}</span>
      <button type="button" className="btn btn-sm" disabled={page >= pages} onClick={() => onPage(page + 1)}>Próxima</button>
    </nav>
  )
}
