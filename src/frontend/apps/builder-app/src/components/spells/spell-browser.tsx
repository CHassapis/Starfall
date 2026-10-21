import { useId, useState, type ReactNode } from "react";
import type { SpellOptionDataModel } from "@starlights/api-client";
import { ElementDetails } from "@/components/element-details";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ordinal } from "@/lib/rules/magic";

import { useUnits } from "@/components/units-provider";
export interface SpellEntry {
  id: string;
  name: string;
  source?: string | null;
  sources?: string[];
  figures?: Partial<SpellOptionDataModel> | null;
  status?: string;
  requirement?: string | null;
  action?: ReactNode;
  details?: ReactNode;
}

/** Shared accessible spell presentation; fetching descriptions waits until the player expands a row. */
export function SpellCard({ entry }: { entry: SpellEntry }) {
  const u = useUnits();
  const [expanded, setExpanded] = useState(false);
  const f = entry.figures;
  return (
    <article className="surface p-3 sm:p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0 flex-1 basis-40">
          <h4 className="font-semibold">{entry.name}</h4>
          <p className="text-sm text-muted-foreground">
            {f?.level === 0 ? "Cantrip" : f?.level != null ? `${ordinal(f.level)} level` : "Spell"}
            {f?.school ? ` · ${f.school}` : ""}
          </p>
        </div>
        {entry.action}
      </div>
      <div className="mt-2 flex flex-wrap gap-x-3 gap-y-1 text-sm text-muted-foreground">
        {entry.status && <span className="font-medium text-primary-text">{entry.status}</span>}
        {f?.concentration && <span>Concentration</span>}
        {f?.ritual && <span>Ritual</span>}
        {f?.castingTime && <span>{f.castingTime}</span>}
        {f?.range && <span>{u.label(f.range)}</span>}
      </div>
      {entry.source && <p className="mt-1 text-sm text-muted-foreground">{entry.source}</p>}
      {entry.requirement && <p className="mt-2 text-sm text-[var(--warning)]">{entry.requirement}</p>}
      <details className="mt-2 text-sm" onToggle={(e) => setExpanded(e.currentTarget.open)}>
        <summary className="py-2 font-medium text-primary-text">
          Spell details<span className="sr-only"> for {entry.name}</span>
        </summary>
        {expanded && (
          <div className="space-y-3 pt-2">
            <dl className="grid grid-cols-1 gap-2 sm:grid-cols-2">
              {[
                ["Components", f?.components],
                ["Duration", f?.duration],
                ["Spell lists", f?.lists?.join(", ")],
              ]
                .filter(([, value]) => value)
                .map(([label, value]) => (
                  <div key={label}>
                    <dt className="text-muted-foreground">{label}</dt>
                    <dd>{value}</dd>
                  </div>
                ))}
            </dl>
            {entry.details}
            <ElementDetails id={entry.id} compact />
          </div>
        )}
      </details>
    </article>
  );
}

/** Pagination uses the document scroll, never an inner scrolling list. */
export function SpellBrowser({ entries, label }: { entries: SpellEntry[]; label: string }) {
  const id = useId();
  const [query, setQuery] = useState("");
  const [level, setLevel] = useState("");
  const [school, setSchool] = useState("");
  const [source, setSource] = useState("");
  const [page, setPage] = useState(0);
  const schools = [...new Set(entries.map((e) => e.figures?.school).filter((s): s is string => !!s))].sort();
  const sources = [...new Set(entries.flatMap((e) => e.sources ?? (e.source ? [e.source] : [])))].sort();
  const levels = [...new Set(entries.map((e) => e.figures?.level).filter((l): l is number => l != null))].sort((a, b) => a - b);
  const visible = entries.filter(
    (e) =>
      (!query.trim() || e.name.toLowerCase().includes(query.trim().toLowerCase())) &&
      (!level || String(e.figures?.level) === level) &&
      (!school || e.figures?.school === school) &&
      (!source || e.source === source || e.sources?.includes(source)),
  );
  const pages = Math.max(1, Math.ceil(visible.length / 8));
  const current = Math.min(page, pages - 1);
  function change(update: () => void) {
    update();
    setPage(0);
  }
  function move(next: number) {
    setPage(next);
    document.getElementById(id)?.scrollIntoView({ block: "start" });
  }
  return (
    <div className="spell-browser space-y-3" id={id} style={{ scrollMarginTop: "6rem" }}>
      <div className="grid gap-3 sm:grid-cols-3">
        <label className="text-sm sm:col-span-3">
          Search {label}
          <Input className="mt-1" value={query} onChange={(e) => change(() => setQuery(e.target.value))} placeholder="Spell name…" />
        </label>
        <label className="min-w-0 text-sm">
          Spell level
          <select value={level} onChange={(e) => change(() => setLevel(e.target.value))}>
            <option value="">All levels</option>
            {levels.map((n) => (
              <option key={n} value={n}>
                {n === 0 ? "Cantrips" : `${ordinal(n)} level`}
              </option>
            ))}
          </select>
        </label>
        <label className="min-w-0 text-sm">
          School
          <select value={school} onChange={(e) => change(() => setSchool(e.target.value))}>
            <option value="">All schools</option>
            {schools.map((s) => (
              <option key={s}>{s}</option>
            ))}
          </select>
        </label>
        <label className="min-w-0 text-sm">
          Source
          <select value={source} onChange={(e) => change(() => setSource(e.target.value))}>
            <option value="">All sources</option>
            {sources.map((s) => (
              <option key={s}>{s}</option>
            ))}
          </select>
        </label>
      </div>
      <div className="flex flex-wrap items-center justify-between gap-2 text-sm">
        <p role="status">
          {visible.length} {visible.length === 1 ? "spell" : "spells"}
          {visible.length > 8 ? ` · Page ${current + 1} of ${pages}` : ""}
        </p>
        {(query || level || school || source) && (
          <Button
            variant="ghost"
            onClick={() =>
              change(() => {
                setQuery("");
                setLevel("");
                setSchool("");
                setSource("");
              })
            }
          >
            Clear filters
          </Button>
        )}
      </div>
      {visible.length ? (
        <div className="grid items-start gap-3 xl:grid-cols-2">
          {visible.slice(current * 8, (current + 1) * 8).map((entry) => (
            <SpellCard key={entry.id} entry={entry} />
          ))}
        </div>
      ) : (
        <p className="rounded-lg bg-muted p-4 text-sm">No spells match these filters. Try another search or clear the filters.</p>
      )}
      {pages > 1 && (
        <nav className="flex flex-wrap items-center justify-between gap-2" aria-label={`${label} pages`}>
          <Button variant="outline" disabled={current === 0} onClick={() => move(current - 1)}>
            Previous
          </Button>
          <span className="text-sm">
            {current + 1} / {pages}
          </span>
          <Button variant="outline" disabled={current === pages - 1} onClick={() => move(current + 1)}>
            Next
          </Button>
        </nav>
      )}
    </div>
  );
}
