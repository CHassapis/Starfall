/// <reference types="node" />
import { readdirSync, readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import type { SheetData } from "@/lib/api/sheet";
import { detailsValues } from "@/lib/aurora-sheet";
import { buildBattleModel } from "@/lib/battle-model";
import type { CalculationTrace } from "@/lib/contracts/calculation-trace";
import { checkShape, type OpenApiDocument } from "@/lib/contracts/openapi-contract";
import { effectFor, effectiveArmorClass } from "@/lib/rules/battle";
import { auroraAttackList, type AuroraAttack, type CustomAttack, type Inventory, type ItemInfo } from "@/lib/rules/items";
import { spellSlots, type Caster } from "@/lib/rules/magic";
import { replay, TraceRecorder } from "@/lib/rules/trace";
import { sheetFigures, spellcastingFigures, type SheetFigureInput, type SheetFigures } from "@/lib/sheet-figures";

/*
 * The reference characters (reference-characters/README.md) through the web app's calculations. The server suite
 * checks the server's figures against the same files; here those hand-worked figures (abilities, statistics,
 * skills, registered elements) and the inventory are the input, and what the web app works out from them is checked
 * against the figures worked out by hand: the sheet, the PDF's fields, the battle view, and their traces.
 */

const root = new URL("../../reference-characters/", import.meta.url);
const read = (path: string) => readFileSync(new URL(path, root), "utf8");
const openapi = JSON.parse(readFileSync(new URL("../../../../../../docs/api/openapi.json", import.meta.url), "utf8")) as OpenApiDocument;

interface Figure {
  value: number;
  why?: string;
}
interface Expected {
  level: number;
  proficiencyBonus: Figure;
  abilities: Record<string, { score: number; modifier: number }>;
  skills: Record<string, Figure>;
  statistics: Record<string, Figure>;
  registered: string[];
  classes: { name: string; level: number; hitDie: number }[];
  spellcasting?: Record<string, { ability: string; attack: number; dc: number; multiclass: Caster["multiclass"]; classLevel: number; edition: "2014" | "2024"; slots?: Record<string, number> }>;
  spellSlots?: { value: Record<string, number> };
  armorClass: Figure;
  hitPoints: Figure;
  initiative: Figure;
  speed: { walk: number; fly?: number; climb?: number; swim?: number };
  passivePerception: Figure;
  attacks: { name: string; attack: string; damage: string; custom?: boolean }[];
  attacksPerAction: number;
  traces?: { value: string; final: number; excluded: { reason: string; value: number }[]; operations?: string[]; userSupplied?: boolean }[];
}
interface InventoryStep {
  id: string;
  item: string;
  base?: string;
  equipped?: string;
  attuned?: boolean;
  attack?: number;
}
interface Step {
  inventory?: InventoryStep[];
  customAttacks?: CustomAttack[];
  story?: Record<string, string>;
}
interface Spec {
  id: string;
  edition: "2014" | "2024";
  steps: Step[];
  expected: Expected;
  scenarios?: { id: string; steps: Step[]; expected: Partial<Expected> & { absentStatistics?: string[] } }[];
}

const specs = readdirSync(new URL("characters/", root))
  .filter((f) => f.endsWith(".json"))
  .sort()
  .map((f) => JSON.parse(read(`characters/${f}`)) as Spec);

// the item catalog as the server describes the reference items (the server suite checks it agrees)
const catalog = new Map(
  (JSON.parse(read("catalog.json")).items as ItemInfo[]).map((i) => [i.id, { ...i, categories: i.categories ?? [] }] as [string, ItemInfo]),
);

// the reference content's element names by id: the sheet counts Extra Attack by its name
const names = new Map<string, string>();
for (const file of readdirSync(new URL("content/", root)).filter((f) => f.endsWith(".xml"))) {
  for (const tag of read(`content/${file}`).matchAll(/<element\b[^>]*>/g)) {
    const name = tag[0].match(/\bname="([^"]*)"/)?.[1];
    const id = tag[0].match(/\bid="([^"]*)"/)?.[1];
    if (name && id) names.set(id, name);
  }
}

interface Built {
  inventory: Inventory;
  hitPointRolls?: string;
}

/** The character after the steps: its inventory (custom attacks stay unless a step lists them) and its story. */
function build(before: Built, steps: Step[]): Built {
  let built = before;
  for (const step of steps) {
    if (step.inventory) {
      const items = step.inventory.map((i) => ({
        id: i.id,
        elementId: i.item,
        quantity: 1,
        ...(i.base ? { baseElementId: i.base } : {}),
        ...(i.equipped ? { equipped: i.equipped } : {}),
        ...(i.attuned ? { attuned: true } : {}),
        ...(i.attack ? { attack: i.attack } : {}),
      }));
      built = { ...built, inventory: { ...built.inventory, items, customAttacks: step.customAttacks ?? built.inventory.customAttacks } as Inventory };
    }
    if (step.story?.hitPointRolls) built = { ...built, hitPointRolls: step.story.hitPointRolls };
  }
  return built;
}

/** A scenario's expectations over the ones before, as the server suite merges them. */
function merge(before: Expected, change: Partial<Expected> & { absentStatistics?: string[] }): Expected {
  const merged = structuredClone(before) as Expected & Record<string, unknown>;
  for (const [key, value] of Object.entries(change)) {
    if (["abilities", "saves", "skills", "statistics", "spellcasting"].includes(key) && merged[key]) Object.assign(merged[key] as object, structuredClone(value));
    else merged[key] = structuredClone(value);
  }
  for (const name of change.absentStatistics ?? []) delete merged.statistics[name];
  return merged;
}

/** What the server gives, as the hand-worked figures say, and the inventory. */
function inputOf(e: Expected, built: Built): SheetFigureInput {
  expect(e.skills.Perception, "every reference character lists its Perception (passive Perception is worked out from it)").toBeDefined();
  return {
    abilities: Object.entries(e.abilities).map(([abbreviation, a]) => ({ abbreviation, calculatedScore: a.score, calculatedModifier: a.modifier })),
    statistics: [{ groupName: "proficiency", totalValue: e.proficiencyBonus.value }, ...Object.entries(e.statistics).map(([groupName, s]) => ({ groupName, totalValue: s.value }))],
    has: new Set(e.registered),
    registrationNames: e.registered.map((id) => names.get(id) ?? id),
    inventory: built.inventory,
    catalog,
    perception: e.skills.Perception?.value,
    level: e.level,
    hitDie: e.classes[0]?.hitDie ?? null,
    classes: e.classes.map((c) => ({ name: c.name, level: c.level, die: c.hitDie })),
    hitPointRolls: built.hitPointRolls,
  };
}

const sign = (n: number) => (n >= 0 ? `+${n}` : `${n}`);
const bonusOf = (text: string) => Number(text.replace(/−/g, "-").match(/^\s*\d*d\d+\s*([+-]\s*\d+)?/i)?.[1]?.replace(/\s/g, "") ?? 0);
const comparable = (f: SheetFigures) => JSON.stringify({ ...f, facts: undefined });
const nested = (t: CalculationTrace): CalculationTrace[] => [t, ...(t.steps ?? []).flatMap((s) => (s.input ? nested(s.input) : []))];
const ABILITY: Record<string, string> = { Strength: "STR", Dexterity: "DEX", Constitution: "CON", Intelligence: "INT", Wisdom: "WIS", Charisma: "CHA" };

/** The sheet data the PDF and the battle view read, from the figures and what the server gives. */
function sheetDataOf(spec: Spec, e: Expected, f: SheetFigures): SheetData {
  const mod = (a: string) => e.abilities[a]?.modifier ?? 0;
  const casters = Object.entries(e.spellcasting ?? {});
  const server = casters.map(([name, c]) => ({ name, attack: c.attack, dc: c.dc }));
  return {
    name: spec.id,
    story: {},
    vision: [],
    features: [],
    speciesTraits: [],
    backgroundFeature: null,
    items: [],
    saves: [],
    edition: spec.edition,
    proficiencyBonus: f.proficiencyBonus,
    abilities: Object.entries(e.abilities).map(([abbreviation, a]) => ({ abilityScoreId: abbreviation, name: abbreviation, abbreviation, baseScore: a.score, additionalScore: 0, calculatedScore: a.score, calculatedModifier: a.modifier })),
    skills: Object.entries(e.skills).map(([name, s]) => ({ name, abilityScoreAbbreviation: "", abilityScoreModifier: 0, additionalBonus: 0, calculatedBonus: s.value, proficiency: "none" })),
    armorClass: f.armorClass,
    armor: f.armor,
    hitPoints: f.hitPoints,
    initiative: f.initiative,
    speeds: f.speeds,
    passivePerception: f.passivePerception,
    attacks: f.attacks,
    attackModes: f.attackModes,
    attacksPerAction: f.attacksPerAction,
    // each spellcasting as the sheet works it out, and one spell of its own for the battle view to cast with it
    spellcasting: casters.map(([name, c]) => ({ name, ability: c.ability, ...spellcastingFigures(name, mod(ABILITY[c.ability]), f.proficiencyBonus, server), prepare: null, slots: {}, spells: [] })),
    cardSpells: casters.map(([name]) => ({ name: `${name} test spell`, level: 1, school: "Evocation", time: "1 action", range: "60 feet", duration: "Instantaneous", components: "V", concentration: false, ritual: false, description: "", origin: `Level 1: Spellcasting (${name})`, source: "" })),
  } as unknown as SheetData;
}

function check(spec: Spec, stage: string, e: Expected, built: Built) {
  const at = `${spec.id} ${stage}`;
  const input = inputOf(e, built);
  const figures = sheetFigures(input);

  // the sheet's figures
  expect(figures.armorClass, `${at} armor class`).toBe(e.armorClass.value);
  expect(figures.hitPoints, `${at} hit points`).toBe(e.hitPoints.value);
  expect(figures.initiative, `${at} initiative`).toBe(e.initiative.value);
  expect(figures.passivePerception, `${at} passive Perception`).toBe(e.passivePerception.value);
  for (const kind of ["walk", "fly", "climb", "swim"] as const) expect(figures.speeds[kind], `${at} ${kind} speed`).toBe(e.speed[kind] ?? (kind === "walk" ? undefined : 0));
  expect(figures.attacks.map((a) => ({ name: a.name, attack: a.attack, damage: a.damage, ...(a.custom ? { custom: true } : {}) })), `${at} attacks`).toEqual(
    e.attacks.map((a) => ({ name: a.name, attack: a.attack, damage: a.damage, ...(a.custom ? { custom: true } : {}) })),
  );
  expect(figures.attacksPerAction, `${at} attacks per action`).toBe(e.attacksPerAction);
  if (e.spellSlots) {
    const casters = Object.entries(e.spellcasting ?? {}).map(([name, c]) => ({ name, multiclass: c.multiclass, classLevel: c.classLevel, edition: c.edition, slots: c.slots ?? {}, pact: null }) as unknown as Caster);
    expect(spellSlots(casters), `${at} spell slots`).toEqual(e.spellSlots.value);
  }

  // the PDF's fields and the battle view agree with the sheet
  const data = sheetDataOf(spec, e, figures);
  const pdf = detailsValues(data);
  expect(pdf.details_armor_class, `${at} PDF armor class`).toBe(String(e.armorClass.value));
  expect(pdf.details_hp_max, `${at} PDF hit points`).toBe(String(e.hitPoints.value));
  expect(pdf.details_initiative, `${at} PDF initiative`).toBe(sign(e.initiative.value));
  expect(pdf.details_passive_perception_total, `${at} PDF passive Perception`).toBe(String(e.passivePerception.value));
  expect(pdf.details_speed_walking, `${at} PDF speed`).toBe(`${e.speed.walk}ft.`);
  expect(pdf.details_encounter_box, `${at} PDF attacks per action`).toBe(`${e.attacksPerAction} ${e.attacksPerAction === 1 ? "Attack" : "Attacks"} / Attack Action`);
  e.attacks.slice(0, 4).forEach((a, i) => expect([pdf[`details_attack${i + 1}_weapon`], pdf[`details_attack${i + 1}_attack`], pdf[`details_attack${i + 1}_damage`]], `${at} PDF attack ${i + 1}`).toEqual([a.name, a.attack, a.damage]));

  const battle = buildBattleModel(data);
  for (const a of e.attacks) {
    const weapon = battle.weapons.find((w) => w.name === a.name && w.current !== false) ?? battle.weapons.find((w) => w.name === a.name);
    expect(weapon, `${at} battle ${a.name}`).toBeDefined();
    expect(`${sign(weapon!.bonus)} vs AC`, `${at} battle ${a.name} to hit`).toBe(a.attack.replace(/^([+-]\d+)$/, "$1 vs AC"));
    expect(weapon!.roll.bonus, `${at} battle ${a.name} damage`).toBe(bonusOf(a.damage));
  }
  for (const [name, c] of Object.entries(e.spellcasting ?? {})) {
    const spell = battle.spells.find((s) => s.name === `${name} test spell`)!;
    expect([spell.attackBonus, spell.saveDc], `${at} battle ${name} spell attack and DC`).toEqual([c.attack, c.dc]);
  }
  // the play view shows the figure with temporary effects on top; the sheet's figure and its trace stay the baseline
  const armorFacts = { total: figures.armorClass, base: figures.armor.base, armorKind: figures.armor.armorKind, shield: figures.armor.hasShield };
  expect(effectiveArmorClass(armorFacts, []).total, `${at} AC without effects`).toBe(e.armorClass.value);
  const shieldSpell = effectFor("shield", { edition: spec.edition, mod: (a) => e.abilities[a]?.modifier ?? 0, proficiencyBonus: figures.proficiencyBonus, rageDamage: 0 })!;
  expect(effectiveArmorClass(armorFacts, [shieldSpell]).total, `${at} AC with the Shield spell`).toBe(e.armorClass.value + 5);

  // traces: written down by the same calculation, which they do not change, the same every time
  const recorder = new TraceRecorder();
  expect(comparable(sheetFigures(input, recorder)), `${at} figures with a recorder`).toBe(comparable(figures));
  const again = new TraceRecorder();
  sheetFigures(input, again);
  expect(JSON.stringify(again.keys().map((k) => again.get(k))), `${at} traces twice`).toBe(JSON.stringify(recorder.keys().map((k) => recorder.get(k))));

  const shown: Record<string, number | null> = {
    "armor-class": figures.armorClass,
    "hit-points": figures.hitPoints,
    initiative: figures.initiative,
    "passive-perception": figures.passivePerception,
    ...Object.fromEntries((["walk", "fly", "climb", "swim"] as const).map((k) => [`speed:${k}`, figures.speeds[k]])),
    ...Object.fromEntries(figures.attacks.flatMap((a) => [[`attack:${a.traceId}:to-hit`, Number(a.attack.match(/[+-]?\d+/)?.[0])], [`attack:${a.traceId}:damage`, bonusOf(a.damage)]])),
  };
  for (const [key, value] of Object.entries(shown)) {
    const trace = recorder.get(key);
    expect(trace, `${at} ${key} is traced`).toBeDefined();
    expect(trace!.final, `${at} ${key} trace ends at the figure shown`).toBe(value);
  }
  for (const key of recorder.keys()) {
    const trace = recorder.get(key)!;
    expect(checkShape(openapi, "CalculationTrace", trace), `${at} ${key} has the contract's shape`).toEqual([]);
    for (const t of nested(trace)) {
      expect(replay(t.steps ?? []), `${at} ${key}: ${t.key} replays to its value`).toBe(t.final);
      expect(t.notes ?? [], `${at} ${key}: ${t.key}`).not.toContainEqual(expect.stringContaining("The steps come to"));
    }
  }

  // the traces the character file names (server keys are the server suite's)
  for (const expected of (e.traces ?? []).filter((t) => /^(armor-class|hit-points|initiative|passive-perception|speed:|attack:)/.test(t.value))) {
    const trace = recorder.get(expected.value);
    expect(trace, `${at} ${expected.value}`).toBeDefined();
    expect(trace!.final, `${at} ${expected.value} final`).toBe(expected.final);
    const left = (list: { reason: string; value: number }[]) => list.map((x) => `${x.reason} ${x.value}`).sort();
    expect(left(trace!.excluded ?? []), `${at} ${expected.value} left out`).toEqual(left(expected.excluded));
    const operations = new Set(nested(trace!).flatMap((t) => (t.steps ?? []).map((s) => s.operation)));
    for (const op of expected.operations ?? []) expect(operations.has(op as never), `${at} ${expected.value} uses ${op}`).toBe(true);
    if (expected.userSupplied) expect(trace!.steps?.every((s) => s.source?.kind === "user-supplied"), `${at} ${expected.value} is as the player wrote it`).toBe(true);
  }
}

describe("reference characters through the web app's calculations", () => {
  it("there are ten, each with a scenario or more among them", () => {
    expect(specs.length).toBe(10);
    expect(specs.flatMap((s) => s.scenarios ?? []).length).toBeGreaterThanOrEqual(6);
  });

  for (const spec of specs) {
    it(`${spec.id}: the sheet, the PDF and the battle view have the figures worked out by hand`, () => {
      let built = build({ inventory: { revision: 1, items: [], coins: {} } as unknown as Inventory }, spec.steps);
      let expected = spec.expected;
      check(spec, "built", expected, built);
      for (const scenario of spec.scenarios ?? []) {
        built = build(built, scenario.steps);
        expected = merge(expected, scenario.expected);
        check(spec, scenario.id, expected, built);
      }
    });
  }

  it("c10's Aurora file gives the attack list and rolled hit points its steps use", () => {
    // the importer reads the file with the browser's DOMParser, which these tests do not have: the attributes are read
    // here, and what the importer does with them (lib/rules/items auroraAttackList) runs as it does in the browser
    const file = read("aurora/imported-rogue.dnd5e");
    const attr = (tag: string, name: string) => tag.match(new RegExp(`\\b${name}="([^"]*)"`))?.[1];
    const attacks: AuroraAttack[] = [...file.matchAll(/<attack\b([^>]*?)(?:\/>|>([\s\S]*?)<\/attack>)/g)].map((m) => ({
      identifier: attr(m[1], "identifier") ?? "",
      name: attr(m[1], "name") ?? "",
      range: attr(m[1], "range"),
      attack: attr(m[1], "attack"),
      damage: attr(m[1], "damage"),
      ability: attr(m[1], "ability"),
      description: m[2]?.match(/<description>([\s\S]*?)<\/description>/)?.[1] ?? null,
      displayed: attr(m[1], "displayed") !== "false",
    }));
    const items = new Set([...file.matchAll(/<item\b[^>]*\bidentifier="([^"]*)"/g)].map((m) => m[1]));
    const c10 = specs.find((s) => s.id === "c10-imported-aurora-rogue")!;
    const step = c10.steps.find((s) => s.inventory)!;

    const { places, customAttacks } = auroraAttackList(attacks, items);
    expect(Object.fromEntries(places)).toEqual(Object.fromEntries([...step.inventory!.filter((i) => i.attack).map((i) => [i.id, i.attack]), ...step.customAttacks!.map((a) => [a.id, a.place])]));
    expect(customAttacks).toEqual(step.customAttacks);
    expect(file.match(/\brndhp="([^"]*)"/)?.[1]).toBe(c10.steps.find((s) => s.story)?.story?.hitPointRolls);
  });
});
