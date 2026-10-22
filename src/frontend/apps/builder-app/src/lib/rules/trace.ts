import type { CalculationTrace, TraceExclusion, TraceExclusionReason, TraceOperation, TraceSource, TraceStep } from "@/lib/contracts/calculation-trace";

/**
 * Calculation traces for the figures the web app works out itself (armor class, hit points, initiative, speeds,
 * passive Perception, attacks), in the server's contract (GET /api/characters/{id}/calculation/trace). The functions
 * that work a figure out take an optional recorder as their last argument and write down what they do as they do it;
 * the figure itself never depends on the recorder, so the same call with or without one gives the same figures.
 *
 * Sources follow the server's: "calculated" with `refers` naming the statistic a value is read from ("ac:misc",
 * "dexterity:modifier"; a skill by its key, "skill:perception"), so the server's trace of it can be asked for;
 * "rule" for a figure of the content (an armor's AC, a class's hit die); "user-supplied" for what the player entered
 * (rolled hit points, an attack written by hand); "fixed" for a number of the rules themselves.
 */

export const CALCULATION_TRACE_VERSION = 1;

/** The traces written down while figures were worked out, by key ("armor-class", "attack:<id>:to-hit"). */
export class TraceRecorder {
  private readonly traces = new Map<string, CalculationTrace>();

  begin(key: string, label: string): TraceBuilder {
    return new TraceBuilder(key, label, (t) => this.traces.set(key, t));
  }

  get(key: string): CalculationTrace | undefined {
    return this.traces.get(key);
  }

  keys(): string[] {
    return [...this.traces.keys()];
  }
}

/** One trace, step by step: each step's result follows from the one before. */
export class TraceBuilder {
  private readonly steps: TraceStep[] = [];
  private readonly excluded: TraceExclusion[] = [];
  private readonly notes: string[] = [];
  private value = 0;
  private readonly key: string;
  private readonly label: string;
  private readonly done?: (trace: CalculationTrace) => void;

  constructor(key: string, label: string, done?: (trace: CalculationTrace) => void) {
    this.key = key;
    this.label = label;
    this.done = done;
  }

  private step(operation: TraceOperation, value: number, label: string, source?: TraceSource, input?: CalculationTrace): this {
    this.value = apply(operation, this.value, value);
    this.steps.push({ operation, value, result: this.value, label, ...(source ? { source } : {}), ...(input ? { input } : {}) });
    return this;
  }

  start(value: number, label: string, source?: TraceSource, input?: CalculationTrace) {
    return this.step("start", value, label, source, input);
  }
  /** Adds the value; the first value of an empty trace starts it. */
  add(value: number, label: string, source?: TraceSource, input?: CalculationTrace) {
    return this.step(this.steps.length ? "add" : "start", value, label, source, input);
  }
  maximum(value: number, label: string, source?: TraceSource) {
    return this.step("maximum", value, label, source);
  }
  minimum(value: number, label: string, source?: TraceSource) {
    return this.step("minimum", value, label, source);
  }
  multiply(value: number, label: string, source?: TraceSource) {
    return this.step("multiply", value, label, source);
  }
  divide(value: number, label: string, source?: TraceSource) {
    return this.step("divide", value, label, source);
  }
  roundUp(label = "Rounded up") {
    return this.step("round-up", this.value, label, fixed("Rounding"));
  }

  exclude(reason: TraceExclusionReason, value: number, label: string, detail?: string, source?: TraceSource): this {
    this.excluded.push({ reason, value, label, ...(source ? { source } : {}), ...(detail ? { detail } : {}) });
    return this;
  }

  note(text: string): this {
    this.notes.push(text);
    return this;
  }

  /** The trace, ending at the figure worked out; a note says so when the steps do not come to it (as the server's). */
  finish(final: number): CalculationTrace {
    const replayed = replay(this.steps);
    const notes = replayed === final ? this.notes : [...this.notes, `The steps come to ${replayed}, not ${final}: part of how this value was worked out is not in the trace.`];
    const trace: CalculationTrace = { key: this.key, label: this.label, final, steps: this.steps, excluded: this.excluded, notes };
    this.done?.(trace);
    return trace;
  }
}

function apply(operation: TraceOperation, value: number, operand: number): number {
  switch (operation) {
    case "start":
    case "replace":
      return operand;
    case "add":
      return value + operand;
    case "maximum":
      return Math.max(value, operand);
    case "minimum":
      return Math.min(value, operand);
    case "multiply":
      return value * operand;
    case "divide":
      return operand === 0 ? value : value / operand;
    case "round-down":
      return Math.floor(value);
    case "round-up":
      return Math.ceil(value);
    case "round-toward-zero":
      return Math.trunc(value);
  }
}

/** The value the steps come to, done one after another (the server's CalculationTraceBuilder.Replay). */
export function replay(steps: readonly TraceStep[]): number {
  return steps.reduce((value, s) => apply(s.operation, value, s.value), 0);
}

/** A value read from a statistic (or a skill's key), whose own trace the server gives. */
export const calculated = (refers: string): TraceSource => ({ kind: "calculated", refers });
export const fixed = (name: string): TraceSource => ({ kind: "fixed", name });
export const userSupplied = (name: string): TraceSource => ({ kind: "user-supplied", name });

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** A figure of the content: an item's (its element when known) or a class's. */
export const fromContent = (name: string, elementType?: string, elementId?: string | null): TraceSource => ({
  kind: "rule",
  name,
  ...(elementType ? { elementType } : {}),
  ...(elementId && GUID.test(elementId) ? { elementId } : {}),
});

const ABILITY = { STR: "strength", DEX: "dexterity", CON: "constitution", INT: "intelligence", WIS: "wisdom", CHA: "charisma" } as const;

/** The statistic of an ability's modifier ("DEX" -> "dexterity:modifier"). */
export const abilityModifier = (abbreviation: keyof typeof ABILITY) => calculated(`${ABILITY[abbreviation]}:modifier`);

/** The key a step's `refers` is traced under: an ability's score or modifier, the proficiency bonus, a skill, a statistic. */
export function traceKeyOf(refers: string): string {
  const name = refers.replace(/^-/, "").toLowerCase();
  const ability = name.match(/^(strength|dexterity|constitution|intelligence|wisdom|charisma):(score|modifier)$/);
  if (ability) return `ability-${ability[2]}:${ability[1]}`;
  if (name === "proficiency") return "proficiency-bonus";
  if (/^(skill|saving-throw|ability-score|ability-modifier|spell-attack|spell-save-dc):/.test(name)) return name;
  return `statistic:${name}`;
}

/** A value the web app works out and traces itself (the server answers "unsupported" for these). */
export const isWebTraceKey = (key: string) =>
  /^(armor-class|hit-points|initiative|passive-perception)$|^speed:(walk|fly|climb|swim)$|^attack:[A-Za-z0-9_-]{1,64}:(to-hit|damage)$|^spell:[^:\s]{1,100}:\d:(dice|bonus)$/.test(key);

/** A name as the server's keys write it ("Sleight of Hand" -> "sleight-of-hand", "Ref Bard" -> "ref-bard"). */
export const slugOf = (name: string) => name.trim().toLowerCase().replace(/ /g, "-");

/** The keys of an attack row's traces (a weapon by its inventory entry, a custom attack by its own id). */
export const attackKey = (id: string, part: "to-hit" | "damage") => `attack:${id}:${part}`;
