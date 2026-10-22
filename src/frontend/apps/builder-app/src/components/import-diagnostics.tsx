import { InfoIcon, TriangleAlertIcon, XCircleIcon } from "lucide-react";
import { diagnosticSummary, diagnosticWhere, type ImportDiagnostic } from "@/lib/import-diagnostics";
import { cn } from "@/lib/utils";

const SEVERITY = {
  error: { icon: XCircleIcon, label: "Problem", tone: "text-destructive" },
  warning: { icon: TriangleAlertIcon, label: "Warning", tone: "text-amber-600 dark:text-amber-400" },
  info: { icon: InfoIcon, label: "Note", tone: "text-muted-foreground" },
} as const;

/**
 * What an import ran into, the same way wherever it is shown: the content update, homebrew files and the Homebrew
 * page, an Aurora character file. A heading that counts them ("3 warnings"), then each with what happened and where
 * (file, element). Open when there are a few; `compact` (inside a list of imported files) keeps it closed and small.
 */
export function ImportDiagnostics({ diagnostics, title, compact, className }: { diagnostics: ImportDiagnostic[]; title?: string; compact?: boolean; className?: string }) {
  if (diagnostics.length === 0) return null;
  const blocking = diagnostics.some((d) => d.severity === "error");
  return (
    <section
      role={blocking ? "alert" : "status"}
      className={cn(
        "min-w-0", compact ? "text-xs text-muted-foreground" : "rounded-md border p-3 text-sm",
        !compact && (blocking ? "border-destructive/40 bg-destructive/5" : "border-amber-500/50 bg-amber-500/5"),
        className,
      )}
    >
      <details open={!compact && diagnostics.length <= 5}>
        <summary className={cn("cursor-pointer", !compact && "font-medium")}>
          {title ? `${title}: ` : ""}
          {diagnosticSummary(diagnostics)}
        </summary>
        <ul className="mt-2 space-y-2">
          {diagnostics.map((d, i) => {
            const { icon: Icon, label, tone } = SEVERITY[d.severity] ?? SEVERITY.info;
            return (
              <li key={`${d.code}-${d.file}-${d.elementId}-${d.detail}-${i}`} className="flex gap-2">
                <Icon className={cn("mt-0.5 size-4 shrink-0", tone)} aria-hidden />
                <div className="min-w-0 [overflow-wrap:anywhere]">
                  <p className={cn(compact ? "" : "text-foreground")}>
                    <span className="sr-only">{label}: </span>
                    {d.message}
                  </p>
                  <p className="text-xs text-muted-foreground">{diagnosticWhere(d)}</p>
                  {!!d.omitted && <p className="text-xs text-muted-foreground">… and {d.omitted} more like this, not listed.</p>}
                </div>
              </li>
            );
          })}
        </ul>
      </details>
    </section>
  );
}
