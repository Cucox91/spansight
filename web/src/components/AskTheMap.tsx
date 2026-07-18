import { useState, type FormEvent } from 'react'
import { askTheMap } from '../api/client'
import { ALL_CONDITIONS, ALL_TYPE_GROUPS, type ConditionOption, type TypeGroup } from '../state/filters'
import { useFilters } from '../state/FiltersContext'

/**
 * FR-AI.1 "Ask the map": one plain-English box that sets the same filter predicate the rail
 * edits — nothing more (ADR-008 §2). The server returns the validated rail-shaped filter plus a
 * code-rendered interpretation, shown here for correction. While Ai:Enabled is false the API
 * answers 503 and this degrades to an explanatory message; filters keep working by hand.
 */
export default function AskTheMap() {
  const { setFilters } = useFilters()
  const [text, setText] = useState('')
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (!text.trim() || busy) return

    setBusy(true)
    setNotice(null)
    try {
      const result = await askTheMap(text.trim())
      // Empty dimensions mean "no constraint" — the rail expresses that as everything selected.
      setFilters((previous) => ({
        ...previous,
        conditions:
          result.filter.conditions.length > 0
            ? (result.filter.conditions as ConditionOption[])
            : ALL_CONDITIONS,
        typeGroups:
          result.filter.typeGroups.length > 0
            ? (result.filter.typeGroups as TypeGroup[])
            : ALL_TYPE_GROUPS,
        state: result.filter.state ?? '',
        yearBuiltMax: result.filter.yearBuiltMax,
        minAdt: result.filter.minAdt ?? 0,
      }))
      setNotice(result.interpretation)
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'AI assist is unavailable right now.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="ask-wrap" onSubmit={submit}>
      <input
        className="search"
        placeholder="Ask the map — e.g. poor truss bridges in Florida"
        aria-label="Ask the map in plain English"
        value={text}
        onChange={(event) => setText(event.target.value)}
        disabled={busy}
      />
      <button type="submit" className="ask-submit" disabled={busy || !text.trim()} aria-label="Ask">
        {busy ? '…' : '→'}
      </button>
      {notice && (
        <output className="ask-notice">
          {notice}
          <button type="button" aria-label="Dismiss" onClick={() => setNotice(null)}>
            ×
          </button>
        </output>
      )}
    </form>
  )
}
