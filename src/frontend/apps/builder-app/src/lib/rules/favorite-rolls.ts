/**
 * Favorite rolls: dice a player names and keeps on the character ("Test Smite: 2d8+3") to roll with one press, on the
 * sheet and in the battle view. They are read and rolled by the compendium's dice (lib/lore/dice), which show each
 * die; the battle view's damage rolls (parseRoll) keep a damage type and never go below 0, which a check must not.
 * They are kept in the character's combat state (CombatState.favoriteRolls), so they go with the character and its
 * export, and need its player's password like everything else there. Keeping rolls at hand is an idea from Flare's
 * pinned rolls (MPL-2.0); no code is taken from it.
 */
import { parseDice, type ParsedDice } from "@/lib/lore/dice";
import type { CombatState } from "./battle";

export interface FavoriteRoll {
  /** stays when the roll is changed */
  id: string;
  name: string;
  /** dice as parseDice reads them: "2d8+3", "d20", "4d6 + 1d4 - 1" */
  formula: string;
}

// the server's limits (CharacterCombat.Validate)
export const MAX_FAVORITE_ROLLS = 40;
export const MAX_ROLL_NAME = 60;
export const MAX_ROLL_FORMULA = 100;

export const newFavoriteRollId = () => `f${Date.now().toString(36)}${Math.random().toString(36).slice(2, 7)}`;

/** The dice of a favorite roll, or null when they are not dice the app can roll or are longer than the server keeps. */
export function favoriteDice(formula: string): ParsedDice | null {
  const f = formula.trim();
  return f.length > MAX_ROLL_FORMULA ? null : parseDice(f);
}

/** The lowest and highest total: "2d8+3" 5 to 19, "1d4-5" −4 to −1. */
export function diceRange(p: ParsedDice): { min: number; max: number } {
  let min = p.bonus;
  let max = p.bonus;
  for (const d of p.dice) {
    min += d.sign > 0 ? d.count : -d.count * d.sides;
    max += d.sign > 0 ? d.count * d.sides : -d.count;
  }
  return { min, max };
}

/** A roll added or changed: its id stays, so it keeps its place in the list. */
export function saveFavoriteRoll(state: CombatState, r: FavoriteRoll): CombatState {
  const list = state.favoriteRolls ?? [];
  return { ...state, favoriteRolls: list.some((x) => x.id === r.id) ? list.map((x) => (x.id === r.id ? r : x)) : [...list, r] };
}

export function removeFavoriteRoll(state: CombatState, id: string): CombatState {
  return { ...state, favoriteRolls: (state.favoriteRolls ?? []).filter((x) => x.id !== id) };
}
