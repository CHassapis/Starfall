/**
 * The group's homebrew made on the Homebrew page: magic items (imported as Aurora items, so the builder treats them
 * like book items) and monsters (stat blocks for campaigns' encounters). Reading is open; saving needs the admin.
 */
import { ApiError } from "@starlights/api-client";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "@/lib/api-client";
import type { HomebrewEditPreview } from "@/lib/contracts/homebrew-preview";
import { homebrewSources, type SourceBook } from "@/lib/api/sources";
import type { HomebrewMonster } from "@/lib/rules/homebrew";

export interface HomebrewItem {
  id?: string | null;
  name: string;
  source?: string | null;
  kind: string;
  rarity: string;
  attunement: boolean;
  attunementBy?: string | null;
  charges?: number | null;
  base?: string | null;
  /** a magic weapon's own damage dice ("1d6"), instead of its base weapon's */
  damage?: string | null;
  weight?: number | null;
  cost?: number | null;
  description: string;
}

/** A feature a homebrew class or subclass gains at a level. */
export interface HomebrewFeature {
  id?: string | null;
  level: number;
  name: string;
  description: string;
}

export type SpellRoll = "none" | "melee-attack" | "ranged-attack" | "save";

/**
 * A spell's mechanics as the form shows them: none at all when it does not say what it rolls, a saving throw's ability
 * and half damage only for a save, and the scaling that fits its level (cantrip tiers, or dice per slot level above).
 */
export function fittingSpellMechanics(spell: HomebrewSpell): HomebrewSpell {
  if (!spell.roll) return { ...spell, roll: null, saveAbility: null, halfOnSave: false, damage: null, healing: null, addsModifier: false, cantripScaling: null, slotScaling: null };
  const save = spell.roll === "save";
  return {
    ...spell,
    saveAbility: save ? spell.saveAbility : null,
    halfOnSave: save && !!spell.halfOnSave,
    cantripScaling: spell.level === 0 ? spell.cantripScaling : null,
    slotScaling: spell.level > 0 ? spell.slotScaling : null,
  };
}

export interface HomebrewSpell {
  id?: string | null;
  name: string;
  source?: string | null;
  /** 0 is a cantrip */
  level: number;
  school: string;
  castingTime: string;
  range: string;
  duration: string;
  verbal: boolean;
  somatic: boolean;
  material: boolean;
  materialText?: string | null;
  concentration: boolean;
  ritual: boolean;
  /** the class spell lists it is on */
  lists: string[];
  description: string;
  higherLevels?: string | null;
  /** what it rolls, said outright (read instead of the text): "melee-attack", "ranged-attack", "save", "none"; empty: not said */
  roll?: SpellRoll | null;
  /** a "save" spell: the ability saved with */
  saveAbility?: string | null;
  /** a "save" spell: a success still takes half the damage */
  halfOnSave?: boolean;
  /** "2d6 fire; 1d6 radiant" */
  damage?: string | null;
  /** "2d8" */
  healing?: string | null;
  /** the spellcasting ability modifier joins the first damage term, or the healing */
  addsModifier?: boolean;
  /** a cantrip: "5:2d10; 11:3d10; 17:4d10" */
  cantripScaling?: string | null;
  /** a leveled spell: the dice each slot level above its own adds, "1d6" */
  slotScaling?: string | null;
  /** the record as saved, when read; a save that sends it back as expectedRevision is refused (409) if it changed since */
  revision?: string | null;
  /** on a save: the revision the edit started from (a preview's) */
  expectedRevision?: string | null;
}

export interface HomebrewSubclass {
  id?: string | null;
  name: string;
  source?: string | null;
  /** the class's subclass choice it joins (its supports) */
  classSupports: string;
  className: string;
  description: string;
  features: HomebrewFeature[];
  /** the record as saved, when read; a save that sends it back as expectedRevision is refused (409) if it changed since */
  revision?: string | null;
  /** on a save: the revision the edit started from (a preview's) */
  expectedRevision?: string | null;
}

export interface HomebrewClass {
  id?: string | null;
  name: string;
  source?: string | null;
  description: string;
  hitDie: number;
  savingThrows: string[];
  armor: string[];
  weapons: string[];
  skillChoices: number;
  skills: string[];
  multiclassArmor: string[];
  multiclassWeapons: string[];
  multiclassAbility?: string | null;
  subclassLevel: number;
  subclassSupports?: string | null;
  asiLevels: number[];
  epicBoon: boolean;
  features: HomebrewFeature[];
  /** None, Full (prepares, like the 2024 Cleric), Half (learns, like the Paladin) or Pact (like the Warlock) */
  casting: string;
  castingAbility?: string | null;
  /** existing lists it also casts from ("Wizard") */
  spellLists: string[];
  cantrips: number;
  spellListName?: string | null;
  /** the record as saved, when read; a save that sends it back as expectedRevision is refused (409) if it changed since */
  revision?: string | null;
  /** on a save: the revision the edit started from (a preview's) */
  expectedRevision?: string | null;
}

/** A class a homebrew subclass can join: its name, book and the supports of its subclass choice. */
export interface SubclassTarget {
  className: string;
  source?: string | null;
  supports: string;
}

export function useHomebrewSpells() {
  return useQuery({ queryKey: ["homebrew", "spells"], queryFn: async () => (await apiClient.get<{ spells: HomebrewSpell[] }>("/api/elements/homebrew/spells")).spells });
}

export function useHomebrewSubclasses() {
  return useQuery({ queryKey: ["homebrew", "subclasses"], queryFn: async () => (await apiClient.get<{ subclasses: HomebrewSubclass[] }>("/api/elements/homebrew/subclasses")).subclasses });
}

export function useHomebrewClasses() {
  return useQuery({ queryKey: ["homebrew", "classes"], queryFn: async () => (await apiClient.get<{ classes: HomebrewClass[] }>("/api/elements/homebrew/classes")).classes });
}

export function useSubclassTargets(enabled = true) {
  return useQuery({
    queryKey: ["homebrew", "subclass-targets"],
    enabled,
    queryFn: async () => (await apiClient.get<{ targets: SubclassTarget[] }>("/api/elements/homebrew/subclass-targets")).targets,
  });
}

export function useHomebrewItems() {
  return useQuery({ queryKey: ["homebrew", "items"], queryFn: async () => (await apiClient.get<{ items: HomebrewItem[] }>("/api/elements/homebrew/items")).items });
}

export function useHomebrewMonsters(enabled = true) {
  return useQuery({ queryKey: ["homebrew", "monsters"], enabled, queryFn: async () => (await apiClient.get<{ monsters: HomebrewMonster[] }>("/api/elements/homebrew/monsters")).monsters });
}

const bookNames = async () => (await apiClient.get<{ sources: SourceBook[] }>("/api/elements/sources")).sources.map((s) => s.name);

/**
 * Sources lists the books a character has switched off, so a homebrew book that did not exist before would be on for
 * every character. After a homebrew save brings in a new book, it is switched off for all of them: a player who wants
 * it ticks it in their Sources.
 */
export async function withNewBooksOff(save: () => Promise<unknown>) {
  const before = new Set(await bookNames());
  await save();
  const { sources } = await apiClient.get<{ sources: SourceBook[] }>("/api/elements/sources");
  const fresh = homebrewSources(sources).filter((name) => !before.has(name));
  if (fresh.length === 0) return;
  const { characters } = await apiClient.get<{ characters: { characterId: string }[] }>("/api/characters");
  for (const c of characters) {
    const path = `/api/characters/${c.characterId}/sources`;
    const { restricted } = await apiClient.get<{ restricted: string[] }>(path);
    const add = fresh.filter((name) => !restricted.includes(name));
    if (add.length) await apiClient.put(path, { restricted: [...restricted, ...add] });
  }
}

export type HomebrewKind = "spells" | "subclasses" | "classes";

/** What saving a spell, subclass or class would do, without saving (admin only). */
export function previewHomebrew<T>(kind: HomebrewKind, body: T): Promise<HomebrewEditPreview> {
  return apiClient.post<T, HomebrewEditPreview>(`/api/elements/homebrew/${kind}/preview`, body);
}

/** A save based on a preview whose record changed (or went away) since: nothing was saved; here is a fresh preview. */
export class StaleHomebrewError extends Error {
  readonly preview: HomebrewEditPreview;

  constructor(preview: HomebrewEditPreview) {
    super(preview.diagnostics.find((d) => d.code === "stale-revision")?.message ?? "It changed after you opened it, so it was not saved.");
    this.name = "StaleHomebrewError";
    this.preview = preview;
  }
}

/** The fresh preview a refused save answers with (409), when that is what the error is. */
export function stalePreviewOf(error: unknown): HomebrewEditPreview | null {
  if (!(error instanceof ApiError) || error.status !== 409) return null;
  try {
    const preview = JSON.parse(error.body) as HomebrewEditPreview;
    return Array.isArray(preview?.diagnostics) ? preview : null;
  } catch {
    return null;
  }
}

/** Saves a spell, subclass or class; a book it brings in starts switched off for every character. */
async function saveNew<T>(kind: HomebrewKind, body: T): Promise<T> {
  let saved: T | undefined;
  try {
    await withNewBooksOff(async () => (saved = await apiClient.put<T, T>(`/api/elements/homebrew/${kind}`, body)));
  } catch (error) {
    const preview = stalePreviewOf(error);
    throw preview ? new StaleHomebrewError(preview) : error;
  }
  return saved!;
}

export function useHomebrewActions() {
  const qc = useQueryClient();
  const refresh = () => {
    for (const key of [["homebrew"], ["compendium"], ["sources"], ["item-catalog"], ["items"], ["builder"]]) qc.invalidateQueries({ queryKey: key }).catch(() => {});
  };
  return {
    saveItem: useMutation({
      mutationFn: async (item: HomebrewItem) => {
        let saved: HomebrewItem | undefined;
        await withNewBooksOff(async () => (saved = await apiClient.put<HomebrewItem, HomebrewItem>("/api/elements/homebrew/items", item)));
        return saved!;
      },
      onSettled: refresh,
    }),
    deleteItem: useMutation({ mutationFn: (id: string) => apiClient.delete(`/api/elements/homebrew/items/${encodeURIComponent(id)}`), onSettled: refresh }),
    saveMonster: useMutation({ mutationFn: (m: HomebrewMonster) => apiClient.put<HomebrewMonster, HomebrewMonster>("/api/elements/homebrew/monsters", m), onSettled: refresh }),
    deleteMonster: useMutation({ mutationFn: (id: string) => apiClient.delete(`/api/elements/homebrew/monsters/${encodeURIComponent(id)}`), onSettled: refresh }),
    saveSpell: useMutation({ mutationFn: (spell: HomebrewSpell) => saveNew<HomebrewSpell>("spells", spell), onSettled: refresh }),
    saveSubclass: useMutation({ mutationFn: (subclass: HomebrewSubclass) => saveNew<HomebrewSubclass>("subclasses", subclass), onSettled: refresh }),
    saveClass: useMutation({ mutationFn: (cls: HomebrewClass) => saveNew<HomebrewClass>("classes", cls), onSettled: refresh }),
    deleteMade: useMutation({
      mutationFn: ({ kind, id }: { kind: "spells" | "subclasses" | "classes"; id: string }) =>
        apiClient.delete(`/api/elements/homebrew/${kind}/${encodeURIComponent(id)}`),
      onSettled: refresh,
    }),
  };
}
