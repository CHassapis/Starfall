import { attackModes, type AttackMode, type Catalog, type CharacterFacts, type Inventory } from "@/lib/rules/items";
import type { Caster } from "@/lib/rules/magic";
import { abilityModifier, calculated, fixed, fromContent, TraceBuilder, TraceRecorder, userSupplied } from "@/lib/rules/trace";
import type { CalculationTrace, TraceSource } from "@/lib/contracts/calculation-trace";
import { sheetArmor, sheetAttackLines, type SheetArmor, type SheetAttackLine } from "@/lib/sheet-model";

/*
 * The sheet's figures that the web app works out itself, from the server's figures (ability modifiers, statistics,
 * the Perception skill) and the inventory: armor class, hit points, initiative, speeds, passive Perception and the
 * attacks. Pure (no React, network or DOM), so the sheet, the PDF, the battle view and the reference tests run the
 * same code. With a recorder, each figure writes down its calculation trace as it is worked out.
 */

type Ability = "STR" | "DEX" | "CON" | "INT" | "WIS" | "CHA";
type Stat = (name: string) => number | undefined;

export interface SheetFigureInput {
  abilities: { abbreviation: string; calculatedScore: number; calculatedModifier: number }[];
  /** the server's statistics (GET /statistics), with where each value comes from */
  statistics: { groupName: string; totalValue: number; values?: { source: string; displayName?: string | null; value: number }[] }[];
  /** Aurora ids of every element the character has (weapon proficiencies, fighting styles) */
  has: ReadonlySet<string>;
  /** the registered elements' names ("Level 5: Extra Attack") */
  registrationNames: string[];
  inventory: Inventory;
  catalog: Catalog;
  /** the Perception skill's bonus as the server works it out */
  perception?: number;
  level: number;
  /** the primary class's hit die; without one the sheet shows no hit points */
  hitDie: number | null;
  /** every class with its level and hit die, the primary first */
  classes: { name: string; level: number; die: number }[];
  /** hit points rolled per level ("8,7": the story's hitPointRolls, Aurora's rndhp) */
  hitPointRolls?: string;
  /** "ac:draconic-resilience" -> "Draconic Resilience (Sorcerer)", for the armor box */
  featureOf?: (statistic: string) => string | undefined;
}

export interface SheetFigures {
  facts: CharacterFacts & { strength: number };
  proficiencyBonus: number;
  initiative: number;
  speeds: { walk: number; fly: number; climb: number; swim: number };
  passivePerception: number;
  armorClass: number;
  armor: SheetArmor;
  attacks: SheetAttackLine[];
  attackModes: AttackMode[];
  attacksPerAction: number;
  hitPoints: number | null;
}

export function sheetFigures(input: SheetFigureInput, trace?: TraceRecorder): SheetFigures {
  const statistics = new Map(input.statistics.map((s) => [s.groupName, s.totalValue]));
  const stat: Stat = (name) => statistics.get(name);
  const mod = (abbreviation: string) => input.abilities.find((a) => a.abbreviation === abbreviation)?.calculatedModifier ?? 0;
  const proficiencyBonus = stat("proficiency") ?? 2;
  const facts: CharacterFacts & { strength: number } = {
    mod: (a: Ability) => mod(a),
    proficiencyBonus,
    stat,
    statNames: input.statistics.map((x) => x.groupName),
    statSource: (n: string) => {
      const value = input.statistics.find((x) => x.groupName === n)?.values?.[0];
      return value?.displayName ?? value?.source ?? undefined;
    },
    has: input.has,
    strength: input.abilities.find((a) => a.abbreviation === "STR")?.calculatedScore ?? 10,
  };
  const armor = sheetArmor(input.inventory, input.catalog, facts, input.featureOf ?? (() => undefined), trace);
  const con = mod("CON");
  return {
    facts,
    proficiencyBonus,
    initiative: initiativeOf(mod, stat, trace),
    speeds: speedsOf(stat, trace),
    passivePerception: passivePerceptionOf(input.perception, mod("WIS"), trace),
    armorClass: armor.total,
    armor,
    attacks: sheetAttackLines(input.inventory, input.catalog, facts, trace),
    attackModes: attackModes(input.inventory, input.catalog, facts),
    attacksPerAction: attacksPerAction(input.registrationNames),
    hitPoints:
      input.classes.length > 1
        ? hitPointsForClasses(input.classes, con, stat("hp") ?? 0, input.hitPointRolls, trace)
        : input.hitDie
          ? hitPointsFor(input.hitDie, input.level, con, stat("hp") ?? 0, input.hitPointRolls, trace, input.classes[0]?.name)
          : null,
  };
}

/** One figure's trace, worked out on demand by the same calculation as the sheet's figures. */
export function traceSheetFigure(input: SheetFigureInput, key: string): CalculationTrace | undefined {
  const trace = new TraceRecorder();
  sheetFigures(input, trace);
  return trace.get(key);
}

/** Initiative: the Dexterity modifier plus initiative bonuses (Alert, a 2024 Alert's proficiency bonus). */
export function initiativeOf(mod: (abbreviation: Ability) => number, stat: Stat, trace?: TraceRecorder): number {
  const bonus = stat("initiative");
  const value = mod("DEX") + (bonus ?? 0);
  const t = trace?.begin("initiative", "Initiative").start(mod("DEX"), "Dexterity modifier", abilityModifier("DEX"));
  if (bonus) t?.add(bonus, "Initiative bonuses", calculated("initiative"));
  t?.finish(value);
  return value;
}

/** Passive Perception: 10 plus the Perception skill's bonus (the Wisdom modifier until the skills have loaded). */
export function passivePerceptionOf(perception: number | undefined, wisdom: number, trace?: TraceRecorder): number {
  const value = 10 + (perception ?? wisdom);
  trace
    ?.begin("passive-perception", "Passive Perception")
    .start(10, "Base", fixed("Passive Perception"))
    .add(perception ?? wisdom, perception === undefined ? "Wisdom modifier" : "Perception", perception === undefined ? abilityModifier("WIS") : calculated("skill:perception"))
    .finish(value);
  return value;
}

/** Attacks in one Attack action: 2 with Extra Attack, more for a high-level fighter. */
export function attacksPerAction(registrationNames: string[]): number {
  return Math.max(
    1,
    ...registrationNames.map((full) => {
      const name = full.replace(/^Level \d+:\s*/, "");
      if (/^Extra Attack$/i.test(name)) return 2;
      const n = name.match(/^Extra Attack \((\d)\)$/i)?.[1] ?? { Two: "2", Three: "3" }[name.match(/^(Two|Three) Extra Attacks$/i)?.[1] ?? ""];
      return n ? 1 + Number(n) : 1;
    }),
  );
}

const SPEED_BONUSES: Record<string, string> = { "innate-speed": "Speed bonuses that depend on what is worn", speed: "Other speed bonuses" };

/**
 * Speeds as Aurora adds them up: the base ("innate speed", or "speed" for a creature form) plus the bonuses that
 * depend on what is worn ("innate speed:misc": Fast Movement, Unarmored Movement) and other bonuses ("speed:misc":
 * Mobile). Climbing, swimming and flying the same, each only when the character has that speed at all.
 */
export function speedsOf(stat: Stat, trace?: TraceRecorder): { walk: number; fly: number; climb: number; swim: number } {
  const plus = (...names: string[]) => names.reduce((n, name) => n + (stat(name) ?? 0), 0);
  const other = (kind: "fly" | "climb" | "swim") => {
    const base = Math.max(stat(`innate-speed:${kind}`) ?? 0, stat(`speed:${kind}`) ?? 0);
    return base > 0 ? base + plus(`innate-speed:${kind}:misc`, `speed:${kind}:misc`) : 0;
  };
  const speeds = {
    walk: (stat("innate-speed") ?? stat("speed") ?? 30) + plus("innate-speed:misc", "speed:misc"),
    fly: other("fly"),
    climb: other("climb"),
    swim: other("swim"),
  };
  if (trace) recordSpeeds(trace, stat, speeds);
  return speeds;
}

function recordSpeeds(trace: TraceRecorder, stat: Stat, speeds: { walk: number; fly: number; climb: number; swim: number }) {
  const bonuses = (t: TraceBuilder, kind?: string) => {
    for (const base of ["innate-speed", "speed"]) {
      const name = kind ? `${base}:${kind}:misc` : `${base}:misc`;
      const value = stat(name);
      if (value) t.add(value, SPEED_BONUSES[base], calculated(name));
    }
  };
  const walk = trace.begin("speed:walk", "Walking speed");
  const base = stat("innate-speed") !== undefined ? "innate-speed" : stat("speed") !== undefined ? "speed" : undefined;
  if (base) walk.start(stat(base)!, base === "innate-speed" ? "Speed" : "Speed (creature form)", calculated(base));
  else walk.start(30, "No speed given: 30 ft", fixed("Speed"));
  bonuses(walk);
  walk.finish(speeds.walk);

  for (const kind of ["fly", "climb", "swim"] as const) {
    const noun = { fly: "Flying", climb: "Climbing", swim: "Swimming" }[kind];
    const t = trace.begin(`speed:${kind}`, `${noun} speed`);
    const innate = stat(`innate-speed:${kind}`);
    const given = stat(`speed:${kind}`);
    if (Math.max(innate ?? 0, given ?? 0) <= 0) {
      t.start(0, `No ${noun.toLowerCase()} speed`, fixed("Speed")).finish(speeds[kind]);
      continue;
    }
    if (innate !== undefined) t.start(innate, noun, calculated(`innate-speed:${kind}`));
    if (given !== undefined && innate !== undefined) t.maximum(given, `${noun} speed given by a feature`, calculated(`speed:${kind}`));
    else if (given !== undefined) t.start(given, noun, calculated(`speed:${kind}`));
    bonuses(t, kind);
    t.finish(speeds[kind]);
  }
}

const rollsOf = (rolls?: string) => (rolls ?? "").split(",").map((r) => Number(r.trim())).filter((n) => n > 0);

/**
 * Maximum hit points: the hit die at level 1, then Aurora's rolls for the levels after when the character came from
 * Aurora with them (its level elements' rndhp, kept by the importer), otherwise the fixed average; the Constitution
 * modifier each level, and bonuses such as Draconic Resilience (the "hp" statistic).
 */
export function hitPointsFor(hitDie: number, level: number, con: number, bonus: number, rolls?: string, trace?: TraceRecorder, className?: string): number {
  const rolled = rollsOf(rolls);
  const dice = rolled.length >= level ? rolled.slice(0, level).reduce((a, b) => a + b, 0) : hitDie + (level - 1) * (hitDie / 2 + 1);
  const total = dice + level * con + bonus;
  if (trace) recordHitPoints(trace.begin("hit-points", "Hit points"), [{ name: className, level, die: hitDie }], con, bonus, rolled, total);
  return total;
}

/**
 * Maximum hit points of a multiclass character: the first class's die at its first level, then each level's fixed
 * average of its own class's die (or Aurora's rolls, when there is one for every level), the Constitution modifier
 * each level, and bonuses.
 */
export function hitPointsForClasses(classes: { level: number; die: number; name?: string }[], con: number, bonus: number, rolls?: string, trace?: TraceRecorder): number {
  const level = classes.reduce((n, c) => n + c.level, 0);
  const rolled = rollsOf(rolls);
  const dice =
    rolled.length >= level
      ? rolled.slice(0, level).reduce((a, b) => a + b, 0)
      : classes.reduce((n, c, i) => n + (i === 0 ? c.die + (c.level - 1) * (c.die / 2 + 1) : c.level * (c.die / 2 + 1)), 0);
  const total = dice + level * con + bonus;
  if (trace) recordHitPoints(trace.begin("hit-points", "Hit points"), classes, con, bonus, rolled, total);
  return total;
}

function recordHitPoints(t: TraceBuilder, classes: { level: number; die: number; name?: string }[], con: number, bonus: number, rolled: number[], total: number) {
  const level = classes.reduce((n, c) => n + c.level, 0);
  const levels: TraceSource = { kind: "character", name: "Character level" };
  if (rolled.length >= level) {
    rolled.slice(0, level).forEach((r, i) => t.add(r, `Rolled for level ${i + 1}`, userSupplied("Rolled hit points")));
  } else {
    if (rolled.length) t.note(`Rolled hit points count only with a roll for every level (${rolled.length} of ${level}), so the fixed average is used.`);
    classes.forEach((c, i) => {
      const die = fromContent(c.name ? `${c.name} hit die (d${c.die})` : `Hit die (d${c.die})`, "Class");
      if (i === 0) t.add(c.die, `Level 1: the whole d${c.die}`, die);
      const after = i === 0 ? c.level - 1 : c.level;
      if (after <= 0) return;
      const average = new TraceBuilder("hit-points:average", `Fixed hit points of ${after} level${after > 1 ? "s" : ""}`)
        .start(c.die, `d${c.die}`, die)
        .divide(2, "Half", fixed("Fixed hit points"))
        .add(1, "Plus 1", fixed("Fixed hit points"))
        .multiply(after, `${after} level${after > 1 ? "s" : ""}`, levels)
        .finish(after * (c.die / 2 + 1));
      t.add(average.final, `${after} more level${after > 1 ? "s" : ""}${c.name ? ` of ${c.name}` : ""} at the d${c.die}'s fixed ${c.die / 2 + 1}`, undefined, average);
    });
  }
  const constitution = new TraceBuilder("hit-points:constitution", "Constitution for each level")
    .start(con, "Constitution modifier", abilityModifier("CON"))
    .multiply(level, `${level} level${level > 1 ? "s" : ""}`, levels)
    .finish(level * con);
  t.add(constitution.final, "Constitution modifier for each level", undefined, constitution);
  if (bonus) t.add(bonus, "Hit point bonuses", calculated("hp"));
  t.finish(total);
}

/**
 * A spellcasting's attack bonus and save DC as the server works them out (with item bonuses such as a rod's,
 * "<class>:spellcasting:attack" and "spellcasting:dc"), found by the spellcasting's name; proficiency plus the ability
 * modifier only for a spellcasting the server has not worked out.
 */
export function spellcastingFigures(name: string, abilityModifier: number, proficiencyBonus: number, server: readonly Pick<Caster, "name" | "attack" | "dc">[] | undefined): { attackBonus: number; saveDc: number } {
  const own = server?.find((c) => c.name.toLowerCase() === name.toLowerCase());
  return own ? { attackBonus: own.attack, saveDc: own.dc } : { attackBonus: proficiencyBonus + abilityModifier, saveDc: 8 + proficiencyBonus + abilityModifier };
}
