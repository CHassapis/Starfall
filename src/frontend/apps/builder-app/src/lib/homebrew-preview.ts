import type { HomebrewEditPreview } from "@/lib/contracts/homebrew-preview";

export type { HomebrewEditPreview } from "@/lib/contracts/homebrew-preview";

// the form's own words for its fields
const FIELD_LABELS: Record<string, string> = {
  name: "Name",
  source: "Book",
  level: "Level",
  school: "School",
  castingTime: "Casting time",
  range: "Range",
  duration: "Duration",
  verbal: "Verbal",
  somatic: "Somatic",
  material: "Material",
  materialText: "Material component",
  concentration: "Concentration",
  ritual: "Ritual",
  lists: "Class spell lists",
  description: "Description",
  higherLevels: "At higher levels",
  classSupports: "Class",
  className: "Class name",
  hitDie: "Hit die",
  savingThrows: "Saving throws",
  armor: "Armor",
  weapons: "Weapons",
  skillChoices: "Skill choices",
  skills: "Skills",
  multiclassArmor: "Multiclass armor",
  multiclassWeapons: "Multiclass weapons",
  multiclassAbility: "Multiclass ability",
  subclassLevel: "Subclass level",
  subclassSupports: "Subclass key",
  asiLevels: "Ability Score Improvement levels",
  epicBoon: "Epic Boon",
  casting: "Spellcasting",
  castingAbility: "Spellcasting ability",
  spellLists: "Spell lists",
  cantrips: "Cantrips",
  spellListName: "Spell list name",
};

/** The form's name for a field the preview reports ("castingTime" → "Casting time"). */
export function fieldLabel(field: string): string {
  return FIELD_LABELS[field] ?? field.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/^./, (c) => c.toUpperCase());
}

/** Whether the save can go ahead: nothing the save refuses (a record changed since is fixed by saving again). */
export function canSave(preview: HomebrewEditPreview): boolean {
  return !preview.diagnostics.some((d) => d.severity === "error" && d.code !== "stale-revision");
}

/** The record's revision as a preview saw it; none for a new one. */
export function expectedRevisionOf(preview: HomebrewEditPreview): string | null {
  return preview.isNew ? null : preview.revision;
}

/**
 * The revision a save sends: the one the form was opened on, so a save someone made since is not overwritten
 * unseen; once a save was refused and its fresh review shown, that review's.
 */
export function revisionToSend(opened: string | null | undefined, refused?: HomebrewEditPreview | null): string | null {
  return refused ? expectedRevisionOf(refused) : (opened ?? null);
}

/** One line on what the save does: "2 changes, 1 removed · 3 characters use it, 1 loses something". */
export function previewSummary(preview: HomebrewEditPreview): string {
  const count = (n: number, one: string, many = `${one}s`) => `${n} ${n === 1 ? one : many}`;
  const removed = preview.elements.filter((e) => e.change === "removed").length;
  // the fields, and the features added, changed or removed; the main element alone when nothing else says why it changed
  const parts = preview.changes.length + preview.elements.filter((e) => e.elementId !== preview.id).length;
  const changes = parts || preview.elements.length;
  const what = preview.isNew ? "New, nothing uses it yet" : changes === 0 ? "No changes" : `${count(changes, "change")}${removed ? `, ${removed} removed` : ""}`;
  const losing = preview.characters.filter((c) => c.loses.length > 0).length;
  const who = preview.characters.length === 0 ? (preview.isNew ? null : "no character uses it") : `${count(preview.characters.length, "character")} ${preview.characters.length === 1 ? "uses" : "use"} it${losing ? `, ${losing} ${losing === 1 ? "loses" : "lose"} something` : ""}`;
  return [what, who].filter(Boolean).join(" · ");
}
