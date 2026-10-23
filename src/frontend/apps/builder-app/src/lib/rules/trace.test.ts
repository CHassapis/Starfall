import { describe, expect, it } from "vitest";
import { armorClass, sheetAttacks, type Catalog, type CharacterFacts, type ItemInfo } from "./items";
import { isWebTraceKey, replay, slugOf, TraceBuilder, TraceRecorder, traceKeyOf } from "./trace";

const catalog: Catalog = new Map<string, ItemInfo>([
  ["scale", { id: "scale", name: "Scale Mail", categories: ["Armor"], armor: { kind: "Medium", armorClass: 14 } } as ItemInfo],
  ["shield", { id: "shield", name: "Shield", categories: ["Armor"], armor: { kind: "Shield", armorClass: 2 } } as ItemInfo],
  ["rapier", { id: "rapier", name: "Rapier", categories: ["Weapon"], weapon: { damage: "1d8", damageType: "piercing", properties: ["Finesse"], martial: true } } as ItemInfo],
]);
const facts = (mods: Record<string, number>, stats: Record<string, number> = {}, has: string[] = []): CharacterFacts => ({
  mod: (a) => mods[a] ?? 0,
  proficiencyBonus: 2,
  stat: (n) => stats[n],
  statNames: Object.keys(stats),
  has: new Set(has),
});
const entry = (id: string, elementId: string, equipped?: string) => ({ id, elementId, quantity: 1, ...(equipped ? { equipped } : {}) });
const inventory = (...items: ReturnType<typeof entry>[]) => ({ revision: 1, items, coins: {} }) as never;

describe("calculation traces of the web app's figures", () => {
  it("replays steps one after another, as the server does", () => {
    const t = new TraceBuilder("x", "X").start(7, "a").add(-10, "b").divide(2, "c").roundUp().multiply(3, "d").minimum(-2, "e").finish(-3);
    expect(t.steps!.map((s) => s.result)).toEqual([7, -3, -1.5, -1, -3, -3]);
    expect(replay(t.steps!)).toBe(-3);
    expect(t.notes).toEqual([]);
  });

  it("says so when its steps do not come to the figure", () => {
    expect(new TraceBuilder("x", "X").start(2, "a").finish(3).notes).toEqual(["The steps come to 2, not 3: part of how this value was worked out is not in the trace."]);
  });

  it("names the server's trace of what a step refers to", () => {
    expect(["dexterity:modifier", "charisma:score", "proficiency", "skill:perception", "ac:misc", "innate-speed"].map(traceKeyOf)).toEqual([
      "ability-modifier:dexterity",
      "ability-score:charisma",
      "proficiency-bonus",
      "skill:perception",
      "statistic:ac:misc",
      "statistic:innate-speed",
    ]);
  });

  it("tells the web app's keys from the server's, as the server does", () => {
    expect(["armor-class", "speed:climb", "attack:4b0e7a10-5c1d-4f2e-9a3b-000000000c10:to-hit", "spell:test-spark:0:dice", "spell:test-salve:3:bonus", "resource:rk2x9abc:max"].every(isWebTraceKey)).toBe(true);
    expect(["skill:stealth", "statistic:ac:misc", "attack:custom:bite:to-hit", "speed:burrow", "spell:test spark:0:dice", "spell:test-spark:10:dice", "spell:test-spark:0:range", "resource:a b:max", "resource:r1:spent"].some(isWebTraceKey)).toBe(false);
    expect(["Sleight of Hand", "Ref Bard"].map(slugOf)).toEqual(["sleight-of-hand", "ref-bard"]);
  });

  it("recording changes no figure", () => {
    const worn = inventory(entry("a", "scale", "Armor"), entry("s", "shield", "Off Hand"), entry("r", "rapier", "Main Hand"));
    const c = facts({ STR: -1, DEX: 3 }, { "ac:misc": 1, "melee:attack": 1 }, ["ID_PROFICIENCY_WEAPON_PROFICIENCY_MARTIAL_WEAPONS"]);
    const trace = new TraceRecorder();
    expect(armorClass(worn, catalog, c, trace)).toEqual(armorClass(worn, catalog, c));
    expect(sheetAttacks(worn, catalog, c, trace)).toEqual(sheetAttacks(worn, catalog, c));
    expect(trace.keys()).toEqual(["armor-class", "attack:r:to-hit", "attack:r:damage"]);
  });

  it("medium armor caps Dexterity with a minimum, and the calculations weighed are kept", () => {
    const trace = new TraceRecorder();
    const ac = armorClass(inventory(entry("a", "scale", "Armor")), catalog, facts({ DEX: 3 }), trace).total;
    const t = trace.get("armor-class")!;
    expect([ac, t.final, replay(t.steps!)]).toEqual([16, 16, 16]);
    expect(t.steps![1].input!.steps!.map((s) => [s.operation, s.value])).toEqual([["start", 3], ["minimum", 2]]);

    const unarmored = new TraceRecorder();
    armorClass(inventory(entry("s", "shield", "Off Hand")), catalog, facts({ DEX: 2 }, { "ac:monk-defense": 15, "ac:draconic-resilience": 11 }), unarmored);
    expect(unarmored.get("armor-class")!.excluded!.map((e) => [e.reason, e.value])).toEqual([
      ["condition-not-met", 15],
      ["lower-alternative", 11],
    ]);
  });

  it("a finesse weapon leaves out the lower of Strength and Dexterity", () => {
    const trace = new TraceRecorder();
    const [rapier] = sheetAttacks(inventory(entry("r", "rapier", "Main Hand")), catalog, facts({ STR: -1, DEX: 4 }, {}, ["ID_PROFICIENCY_WEAPON_PROFICIENCY_MARTIAL_WEAPONS"]), trace);
    const hit = trace.get("attack:r:to-hit")!;
    expect([rapier.toHit, hit.final]).toEqual([6, 6]);
    expect(hit.excluded!.map((e) => [e.reason, e.value])).toEqual([["lower-alternative", -1]]);
    expect(trace.get("attack:r:damage")!.final).toBe(4);
  });

  it("an attack written by hand is traced as the player wrote it", () => {
    const trace = new TraceRecorder();
    const written = { revision: 1, items: [], coins: {}, customAttacks: [{ id: "bite", name: "Bite", toHit: "+5 vs AC", damage: "1d6 + 3 piercing" }] } as never;
    sheetAttacks(written, catalog, facts({}), trace);
    expect([trace.get("attack:bite:to-hit")!.steps![0].source!.kind, trace.get("attack:bite:to-hit")!.final, trace.get("attack:bite:damage")!.final]).toEqual(["user-supplied", 5, 3]);
  });
});
