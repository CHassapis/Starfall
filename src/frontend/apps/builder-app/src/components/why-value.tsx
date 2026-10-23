import { useQuery } from "@tanstack/react-query";
import { CalculatorIcon } from "lucide-react";
import { useState } from "react";
import { Drawer, DrawerContent, DrawerDescription, DrawerHeader, DrawerTitle } from "@/components/ui/drawer";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { Spinner } from "@/components/ui/spinner";
import { useCanHover } from "@/hooks/use-can-hover";
import { apiClient } from "@/lib/api-client";
import type { CalculationTrace, CalculationTraceResponse, TraceExclusionReason, TraceOperation, TraceStep } from "@/lib/contracts/calculation-trace";
import { CALCULATION_TRACE_VERSION, isWebTraceKey, traceKeyOf } from "@/lib/rules/trace";
import { cn } from "@/lib/utils";

/**
 * "Why this value?": a small button beside a figure that shows how it was worked out, step by step, with what was
 * left out and why. The server's figures are asked for when the card opens (GET …/calculation/trace, with the same
 * access as the character's other endpoints); the web app's own (armor class, hit points, attacks) come from
 * `explain`, worked out by the same calculation as the figure. A popover with a mouse, a bottom sheet on a touch
 * screen, like InfoCard. When the figure shown is not the trace's (temporary effects in play), the card says so.
 */
export function WhyValue({
  characterId,
  traceKey,
  label,
  explain,
  shown,
  effects,
  className,
}: {
  characterId: string;
  /** the value's key: "skill:stealth", "armor-class", "attack:<id>:to-hit" */
  traceKey: string;
  /** what the figure is, for the button's name and the card's title ("Armor class") */
  label: string;
  explain?: (key: string) => CalculationTrace | undefined;
  /** the figure shown beside the button */
  shown?: number | null;
  /** temporary effects in the figure shown ("Shield +5"), which the trace leaves out */
  effects?: string[];
  className?: string;
}) {
  const canHover = useCanHover();
  const [open, setOpen] = useState(false);
  const body = open && <TraceCard characterId={characterId} traceKey={traceKey} explain={explain} shown={shown} effects={effects} />;
  const trigger = (
    <button
      type="button"
      aria-label={`Why this value? ${label}`}
      title="Why this value?"
      className={cn(
        "inline-flex size-6 shrink-0 items-center justify-center rounded-md align-middle text-muted-foreground hover:bg-muted hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none",
        className,
      )}
      onClick={canHover ? undefined : () => setOpen(true)}
    >
      <CalculatorIcon className="size-3.5" aria-hidden />
    </button>
  );

  if (!canHover) {
    return (
      <>
        {trigger}
        <Drawer open={open} onOpenChange={setOpen}>
          <DrawerContent>
            <DrawerHeader className="pb-0 text-left">
              <DrawerTitle>Why this value?</DrawerTitle>
              <DrawerDescription>{label}</DrawerDescription>
            </DrawerHeader>
            <div className="max-h-[75vh] overflow-y-auto px-4 pt-2 pb-6">{body}</div>
          </DrawerContent>
        </Drawer>
      </>
    );
  }
  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>{trigger}</PopoverTrigger>
      <PopoverContent className="max-h-[70vh] w-96 max-w-[calc(100vw-2rem)] overflow-y-auto" aria-label={`Why this value? ${label}`}>
        {body}
      </PopoverContent>
    </Popover>
  );
}

function TraceCard({ characterId, traceKey, explain, shown, effects }: { characterId: string; traceKey: string; explain?: (key: string) => CalculationTrace | undefined; shown?: number | null; effects?: string[] }) {
  if (isWebTraceKey(traceKey)) {
    const trace = explain?.(traceKey);
    return trace ? <TraceView trace={trace} characterId={characterId} shown={shown} effects={effects} /> : <p className="text-sm">How this figure is worked out is not recorded.</p>;
  }
  return <ServerTrace characterId={characterId} traceKey={traceKey} shown={shown} effects={effects} />;
}

function ServerTrace({ characterId, traceKey, shown, effects, depth = 0 }: { characterId: string; traceKey: string; shown?: number | null; effects?: string[]; depth?: number }) {
  const query = useQuery({
    queryKey: ["calculation-trace", characterId, traceKey],
    queryFn: () =>
      apiClient.get<CalculationTraceResponse>(`/api/characters/${characterId}/calculation/trace?value=${encodeURIComponent(traceKey)}&version=${CALCULATION_TRACE_VERSION}`),
    staleTime: 0,
  });
  if (query.isLoading) return <Spinner className="mx-auto my-2" />;
  if (query.error || !query.data) return <p role="alert" className="text-sm">Could not load how this was worked out. {query.error?.message}</p>;
  const response = query.data;
  if (response.status !== "traced" || !response.trace) return <p className="text-sm">{response.reason ?? "How this figure is worked out is not recorded."}</p>;
  const sources = response.content?.sources ?? [];
  return (
    <>
      <TraceView trace={response.trace} characterId={characterId} shown={shown} effects={effects} depth={depth} />
      {depth === 0 && sources.length > 0 && <p className="mt-3 text-xs text-muted-foreground">Content: {sources.join(", ")}</p>}
    </>
  );
}

const number = (n: number) => String(Math.round(n * 100) / 100).replace("-", "−");
const signed = (n: number) => (n < 0 ? number(n) : `+${number(n)}`);

const OPERATION: Record<TraceOperation, (value: number) => string> = {
  start: (v) => number(v),
  add: (v) => signed(v),
  replace: (v) => `becomes ${number(v)}`,
  maximum: (v) => `at least ${number(v)}`,
  minimum: (v) => `at most ${number(v)}`,
  multiply: (v) => `× ${number(v)}`,
  divide: (v) => `÷ ${number(v)}`,
  "round-down": () => "rounded down",
  "round-up": () => "rounded up",
  "round-toward-zero": () => "fraction dropped",
};

const LEFT_OUT: Record<TraceExclusionReason, string> = {
  stacking: "the same kind of bonus counts once",
  "lower-alternative": "a higher alternative counts",
  "set-not-higher": "not higher than the score",
  "condition-not-met": "its condition is not met",
  "unresolved-dependency": "it refers to something not worked out",
};

function TraceView({ trace, characterId, shown, effects, depth = 0 }: { trace: CalculationTrace; characterId: string; shown?: number | null; effects?: string[]; depth?: number }) {
  const excluded = trace.excluded ?? [];
  const notes = trace.notes ?? [];
  return (
    <div className="space-y-2 text-sm">
      <p className="font-medium">
        {trace.label}: {number(trace.final)}
      </p>
      {shown !== undefined && shown !== null && shown !== trace.final && (
        <p className="rounded-md bg-muted p-2 text-xs">
          {effects?.length
            ? `Shown as ${number(shown)} with temporary effects (${effects.join(", ")}). This is the sheet's ${number(trace.final)} before them.`
            : `Shown as ${number(shown)}; this comes to ${number(trace.final)}. Reload the sheet if the character just changed.`}
        </p>
      )}
      <ol className="space-y-1">
        {(trace.steps ?? []).map((step, i) => (
          <li key={i}>
            <Step step={step} characterId={characterId} depth={depth} />
          </li>
        ))}
      </ol>
      {excluded.length > 0 && (
        <div>
          <p className="text-xs font-medium text-muted-foreground">Left out</p>
          <ul className="space-y-1">
            {excluded.map((e, i) => (
              <li key={i} className="text-muted-foreground">
                <span className="line-through decoration-muted-foreground/60">
                  {e.label} {signed(e.value)}
                </span>
                : {LEFT_OUT[e.reason]}
                {e.detail ? `. ${e.detail}` : ""}
              </li>
            ))}
          </ul>
        </div>
      )}
      {notes.length > 0 && (
        <ul className="space-y-1 text-xs text-muted-foreground">
          {notes.map((n, i) => (
            <li key={i}>{n}</li>
          ))}
        </ul>
      )}
    </div>
  );
}

const SOURCE: Partial<Record<string, string>> = { "user-supplied": "entered by you", character: "level" };

function Step({ step, characterId, depth }: { step: TraceStep; characterId: string; depth: number }) {
  const [asked, setAsked] = useState(false);
  const source = step.source;
  const from = source?.kind === "rule" && source.name && source.name !== step.label ? source.name : source ? SOURCE[source.kind] : undefined;
  // a value of its own the server traces: shown when asked for
  const refers = !step.input && source?.kind === "calculated" && source.refers ? traceKeyOf(source.refers) : undefined;
  const more = depth < 3 && (step.input || (refers && /^[a-z0-9:'()+_-]+$/.test(refers)));
  return (
    <div>
      <div className="flex items-baseline justify-between gap-3">
        <span className="min-w-0">
          {step.label}
          {from && <span className="text-muted-foreground"> · {from}</span>}
        </span>
        <span className="shrink-0 font-medium tabular-nums">
          {OPERATION[step.operation](step.value)}
          {step.operation !== "start" && <span className="text-muted-foreground"> = {number(step.result)}</span>}
        </span>
      </div>
      {more && (
        <details className="mt-1 ml-1" onToggle={(event) => setAsked(event.currentTarget.open)}>
          <summary className="cursor-pointer py-1 text-xs text-muted-foreground">How {step.input?.label ?? step.label} was worked out</summary>
          <div className="mt-1 border-l pl-3">
            {step.input ? <TraceView trace={step.input} characterId={characterId} depth={depth + 1} /> : asked && refers && <ServerTrace characterId={characterId} traceKey={refers} depth={depth + 1} />}
          </div>
        </details>
      )}
    </div>
  );
}
