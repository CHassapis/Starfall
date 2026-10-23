/// <reference types="node" />
import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { describeRoll, parseDice, roll } from "@/lib/lore/dice";
import { EMPTY_COMBAT, type CombatState } from "./battle";
import { diceRange, favoriteDice, removeFavoriteRoll, saveFavoriteRoll, type FavoriteRoll } from "./favorite-rolls";

// the server reads the same table (CharacterCombatTests), so both sides take the same dice
const cases = JSON.parse(readFileSync(new URL("../lore/dice-cases.json", import.meta.url), "utf8")) as { cases: { formula: string; dice: boolean }[] };
// every die lands on its middle face: 1 + floor(0.5 × sides), a d8 is 5, a d20 is 11, a d4 is 3
const middle = () => 0.5;

describe("what a favorite roll may be", () => {
  it.each(cases.cases)("$formula is dice: $dice", ({ formula, dice }) => {
    expect(parseDice(formula) !== null).toBe(dice);
    expect(favoriteDice(formula) !== null).toBe(dice);
  });

  it("is no longer than the server keeps (100 characters)", () => {
    const long = `1d6${"+1".repeat(49)}`; // 101 characters, dice all the same
    expect(long).toHaveLength(101);
    expect(parseDice(long)).not.toBeNull();
    expect(favoriteDice(long)).toBeNull();
    const fits = `1d6${"+1".repeat(47)}+10`; // 100 characters
    expect(fits).toHaveLength(100);
    expect(favoriteDice(fits)).not.toBeNull();
  });
});

describe("a favorite roll's range and roll", () => {
  it.each([
    ["2d8+3", 5, 19], // 2 + 3 to 16 + 3
    ["1d4-5", -4, -1], // 1 − 5 to 4 − 5
    ["4d6 + 1d4 - 1", 4, 27], // 4 + 1 − 1 to 24 + 4 − 1
    ["d20", 1, 20],
    ["1d6-1d4", -3, 5], // 1 − 4 to 6 − 1
    ["7", 7, 7],
  ])("%s rolls %i to %i", (formula, min, max) => {
    expect(diceRange(favoriteDice(formula)!)).toEqual({ min, max });
  });

  it("rolls with the compendium's dice, each die shown", () => {
    const smite = roll(favoriteDice("2d8+3")!, middle);
    expect(smite.total).toBe(13); // 5 + 5 + 3
    expect(describeRoll(smite)).toBe("5 + 5 + 3 = 13");
    const both = roll(favoriteDice("1d6-1d4")!, middle);
    expect(both.total).toBe(1); // 4 − 3
    expect(describeRoll(both)).toBe("4 − 3 = 1");
    expect(roll(favoriteDice("d20")!, middle).total).toBe(11);
  });
});

describe("keeping favorite rolls in the combat state", () => {
  const smite: FavoriteRoll = { id: "f1", name: "Test Smite", formula: "2d8+3" };
  const luck: FavoriteRoll = { id: "f2", name: "Test Luck", formula: "d20" };

  it("adds them in order, changes one in its place, and removes one", () => {
    let state: CombatState = { ...EMPTY_COMBAT, uses: { Rage: 1 } };
    state = saveFavoriteRoll(saveFavoriteRoll(state, smite), luck);
    expect(state.favoriteRolls).toEqual([smite, luck]);
    state = saveFavoriteRoll(state, { ...smite, formula: "3d8+3" });
    expect(state.favoriteRolls).toEqual([{ ...smite, formula: "3d8+3" }, luck]);
    state = removeFavoriteRoll(state, "f1");
    expect(state.favoriteRolls).toEqual([luck]);
    expect(state.uses).toEqual({ Rage: 1 });
  });

  it("an emptied list is sent as an empty list, which the server keeps as none", () => {
    expect(removeFavoriteRoll({ ...EMPTY_COMBAT, favoriteRolls: [luck] }, "f2").favoriteRolls).toEqual([]);
    expect(removeFavoriteRoll(EMPTY_COMBAT, "f2").favoriteRolls).toEqual([]);
  });
});
