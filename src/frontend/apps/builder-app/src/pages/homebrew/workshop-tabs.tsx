/**
 * The Homebrew page's spells, subclasses and classes: each becomes Aurora content the builder uses like a book's
 * (for characters with Homebrew ticked in their Sources). Everyone can read them; making them needs the admin.
 */
import { PencilIcon, PlusIcon, Trash2Icon, XIcon } from "lucide-react";
import { useMemo, useState, type ReactNode } from "react";
import { toast } from "sonner";
import { RichText } from "@/components/rich-text";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Spinner } from "@/components/ui/spinner";
import {
  useHomebrewActions,
  useHomebrewClasses,
  useHomebrewSpells,
  useHomebrewSubclasses,
  useSubclassTargets,
  type HomebrewClass,
  type HomebrewFeature,
  fittingSpellMechanics,
  type HomebrewSpell,
  type HomebrewSubclass,
  type SpellRoll,
} from "@/lib/api/homebrew";
import { ordinal } from "@/lib/rules/magic";
import { isAdmin } from "@/lib/player";
import { cn } from "@/lib/utils";
import { ReviewFooter } from "./edit-preview";
import { area, select } from "./form-styles";
import { Field } from "./homebrew-tabs";

import { useUnits } from "@/components/units-provider";
const ROLLS: { value: SpellRoll | ""; label: string }[] = [
  { value: "", label: "Not said: read from the text" },
  { value: "ranged-attack", label: "Ranged spell attack" },
  { value: "melee-attack", label: "Melee spell attack" },
  { value: "save", label: "Saving throw" },
  { value: "none", label: "No roll (healing, or no damage)" },
];
const SCHOOLS = ["Abjuration", "Conjuration", "Divination", "Enchantment", "Evocation", "Illusion", "Necromancy", "Transmutation"];
const ABILITIES = ["Strength", "Dexterity", "Constitution", "Intelligence", "Wisdom", "Charisma"];
const SKILLS = ["Acrobatics", "Animal Handling", "Arcana", "Athletics", "Deception", "History", "Insight", "Intimidation", "Investigation", "Medicine", "Nature", "Perception", "Performance", "Persuasion", "Religion", "Sleight of Hand", "Stealth", "Survival"];
const ARMOR = ["Light", "Medium", "Heavy", "Shields"];
const WEAPONS = ["Simple", "Martial"];
const BOOK_LISTS = ["Artificer", "Bard", "Cleric", "Druid", "Paladin", "Ranger", "Sorcerer", "Warlock", "Wizard"];
const CASTING: { value: string; label: string }[] = [
  { value: "None", label: "No spellcasting" },
  { value: "Full", label: "Full caster: prepares spells, like the 2024 Cleric" },
  { value: "Half", label: "Half caster: learns spells, like the 2024 Paladin" },
  { value: "Pact", label: "Pact magic: like the 2024 Warlock" },
];

type Kind = "spells" | "subclasses" | "classes";

function Checks({ label, options, value, onChange, hint }: { label: string; options: string[]; value: string[]; onChange: (v: string[]) => void; hint?: string }) {
  return (
    <fieldset className="space-y-1.5 sm:col-span-2">
      <legend className="text-sm font-medium">{label}</legend>
      <div className="flex flex-wrap gap-1.5">
        {options.map((o) => {
          const on = value.includes(o);
          return (
            <label key={o} className={cn("inline-flex min-h-9 cursor-pointer items-center gap-1.5 rounded-md border px-2.5 text-sm sm:min-h-8", on ? "border-primary/60 bg-primary/10" : "text-muted-foreground")}>
              <input type="checkbox" className="size-4" checked={on} onChange={(e) => onChange(e.target.checked ? [...value, o] : value.filter((v) => v !== o))} />
              {o}
            </label>
          );
        })}
      </div>
      {hint && <p className="text-xs text-muted-foreground">{hint}</p>}
    </fieldset>
  );
}

/** Features by level: what a class or subclass gains, each with its text. */
function Features({ value, onChange }: { value: HomebrewFeature[]; onChange: (v: HomebrewFeature[]) => void }) {
  const set = (i: number, patch: Partial<HomebrewFeature>) => onChange(value.map((f, j) => (j === i ? { ...f, ...patch } : f)));
  return (
    <div className="space-y-2 sm:col-span-2">
      <p className="text-sm font-medium">Features by level</p>
      {value.map((f, i) => (
        <div key={f.id ?? i} className="space-y-1.5 rounded-md border p-2">
          <div className="flex items-center gap-2">
            <Input
              aria-label="Level"
              inputMode="numeric"
              value={f.level}
              onChange={(e) => set(i, { level: Math.min(20, Math.max(1, Number(e.target.value.replace(/\D/g, "")) || 1)) })}
              className="h-9 w-16 text-center"
            />
            <Input aria-label="Feature name" placeholder="Feature name" value={f.name} onChange={(e) => set(i, { name: e.target.value })} maxLength={100} />
            <Button size="icon-sm" variant="ghost" aria-label={`Remove ${f.name || "this feature"}`} onClick={() => onChange(value.filter((_, j) => j !== i))}>
              <XIcon />
            </Button>
          </div>
          <textarea aria-label="What it does" rows={3} className={area} placeholder="What it does" value={f.description} onChange={(e) => set(i, { description: e.target.value })} maxLength={10000} />
        </div>
      ))}
      <Button size="sm" variant="outline" onClick={() => onChange([...value, { level: Math.max(1, ...value.map((f) => f.level)), name: "", description: "" }])}>
        <PlusIcon /> Add a feature
      </Button>
    </div>
  );
}

function Header({ text, newLabel, onNew }: { text: ReactNode; newLabel: string; onNew: () => void }) {
  const admin = isAdmin();
  return (
    <div className="flex flex-wrap items-center gap-3">
      <p className="min-w-0 flex-1 text-sm text-muted-foreground">{text}</p>
      {admin ? (
        <Button onClick={onNew}>
          <PlusIcon /> {newLabel}
        </Button>
      ) : (
        <span className="text-sm text-muted-foreground">Unlock admin (key button at the top) to make homebrew.</span>
      )}
    </div>
  );
}

function RowActions({ name, kind, id, warn, onEdit }: { name: string; kind: Kind; id: string; warn: string; onEdit: () => void }) {
  const { deleteMade } = useHomebrewActions();
  if (!isAdmin()) return null;
  return (
    <>
      <Button size="icon-sm" variant="ghost" aria-label={`Edit ${name}`} onClick={onEdit}>
        <PencilIcon />
      </Button>
      <Button
        size="icon-sm"
        variant="ghost"
        aria-label={`Delete ${name}`}
        onClick={() => {
          if (confirm(`Delete ${name}? ${warn}`)) deleteMade.mutate({ kind, id }, { onError: (e) => toast.error("Could not delete it", { description: e.message }) });
        }}
      >
        <Trash2Icon />
      </Button>
    </>
  );
}

// after a save: the review footer shows what went wrong, if anything did
const saved = (name: string, close: () => void) => () => {
  toast.success(`${name} saved`);
  close();
};

// ---- spells ----

const NEW_SPELL: HomebrewSpell = {
  name: "", source: "Homebrew", level: 1, school: "Evocation", castingTime: "1 action", range: "60 feet", duration: "Instantaneous",
  verbal: true, somatic: true, material: false, concentration: false, ritual: false, lists: [], description: "",
};

export function HomebrewSpells({ startNew }: { startNew?: boolean }) {
  const { data, isLoading } = useHomebrewSpells();
  const classes = useHomebrewClasses();
  const { saveSpell } = useHomebrewActions();
  // a tab mounts when it is shown: ?new=1 opens its form once
  const [editing, setEditing] = useState<HomebrewSpell | null>(() => (startNew && isAdmin() ? { ...NEW_SPELL } : null));
  const spells = data ?? [];
  const lists = [...BOOK_LISTS, ...(classes.data ?? []).map((c) => c.spellListName ?? c.name)];
  return (
    <div className="space-y-4">
      <Header
        text="Spells made here join the class lists you tick: characters of those classes find them in their spell choices (with Homebrew ticked in their Sources), and the simulator casts them like any spell."
        newLabel="New spell"
        onNew={() => setEditing({ ...NEW_SPELL })}
      />
      {isLoading && <Spinner className="mx-auto my-8 size-6" />}
      {!isLoading && spells.length === 0 && <p className="celestial-empty text-sm text-muted-foreground">No homebrew spells yet.</p>}
      <div className="grid gap-3 md:grid-cols-2">
        {spells.map((s) => (
          <article key={s.id} className="rounded-lg border bg-background/60 p-3">
            <div className="flex flex-wrap items-center gap-2">
              <h3 className="font-medium">{s.name}</h3>
              <Badge variant="outline">{s.level === 0 ? "Cantrip" : `${ordinal(s.level)} level`}</Badge>
              <span className="text-xs text-muted-foreground">
                {s.school}
                {s.concentration ? " · Concentration" : ""}
                {s.ritual ? " · Ritual" : ""} · {s.lists.join(", ") || "on no class list"}
              </span>
              <span className="flex-1" />
              <RowActions name={s.name} kind="spells" id={s.id!} warn="Characters who picked it lose it." onEdit={() => setEditing(s)} />
            </div>
            <p className="mt-1 text-xs text-muted-foreground">
              {s.castingTime} · {s.range} · {s.duration} · {[s.verbal && "V", s.somatic && "S", s.material && `M (${s.materialText ?? ""})`].filter(Boolean).join(", ")}
            </p>
            <RichText text={s.description} className="mt-1 text-muted-foreground" />
          </article>
        ))}
      </div>
      {editing && <SpellDialog spell={editing} lists={lists} busy={saveSpell.isPending} onClose={() => setEditing(null)} onSave={(s) => saveSpell.mutateAsync(s).then(saved(s.name, () => setEditing(null)))} />}
    </div>
  );
}

function SpellDialog({ spell, lists, busy, onClose, onSave }: { spell: HomebrewSpell; lists: string[]; busy: boolean; onClose: () => void; onSave: (s: HomebrewSpell) => Promise<unknown> }) {
  const u = useUnits();
  const [f, setF] = useState<HomebrewSpell>(spell);
  const set = (patch: Partial<HomebrewSpell>) => setF((x) => ({ ...x, ...patch }));
  // what is reviewed and saved: the mechanics that fit the roll and level (a review holds while the form is unchanged)
  const model = useMemo(() => fittingSpellMechanics(f), [f]);
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>{spell.id ? `Edit ${spell.name}` : "New spell"}</DialogTitle>
          <DialogDescription>It becomes a spell like the books' ones, on the class lists you tick.</DialogDescription>
        </DialogHeader>
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="Name">
            <Input value={f.name} onChange={(e) => set({ name: e.target.value })} maxLength={100} autoFocus />
          </Field>
          <Field label="Book" hint='Shows as its source, e.g. "Curse of Strahd (homebrew)"'>
            <Input value={f.source ?? ""} onChange={(e) => set({ source: e.target.value })} maxLength={100} />
          </Field>
          <Field label="Level">
            <select className={select} value={f.level} onChange={(e) => set({ level: Number(e.target.value) })}>
              {Array.from({ length: 10 }, (_, l) => (
                <option key={l} value={l}>
                  {l === 0 ? "Cantrip" : `${ordinal(l)} level`}
                </option>
              ))}
            </select>
          </Field>
          <Field label="School">
            <select className={select} value={f.school} onChange={(e) => set({ school: e.target.value })}>
              {SCHOOLS.map((s) => (
                <option key={s}>{s}</option>
              ))}
            </select>
          </Field>
          <Field label="Casting time" hint="e.g. 1 action, 1 bonus action, 1 reaction, 1 minute">
            <Input value={f.castingTime} onChange={(e) => set({ castingTime: e.target.value })} maxLength={100} />
          </Field>
          <Field label="Range" hint={`e.g. Self, Touch, 60 feet${u.units === "metric" ? " (in feet: shown in metres)" : ""}`}>
            <Input value={f.range} onChange={(e) => set({ range: e.target.value })} maxLength={100} />
          </Field>
          <Field label="Duration" hint="e.g. Instantaneous, Concentration, up to 1 minute">
            <Input value={f.duration} onChange={(e) => set({ duration: e.target.value })} maxLength={100} />
          </Field>
          <Checks
            label="Components and tags"
            options={["Verbal", "Somatic", "Material", "Concentration", "Ritual"]}
            value={[f.verbal && "Verbal", f.somatic && "Somatic", f.material && "Material", f.concentration && "Concentration", f.ritual && "Ritual"].filter((x): x is string => !!x)}
            onChange={(v) => set({ verbal: v.includes("Verbal"), somatic: v.includes("Somatic"), material: v.includes("Material"), concentration: v.includes("Concentration"), ritual: v.includes("Ritual") })}
          />
          {f.material && (
            <Field label="Material component" wide>
              <Input value={f.materialText ?? ""} onChange={(e) => set({ materialText: e.target.value })} maxLength={300} placeholder="e.g. a pinch of silver dust" />
            </Field>
          )}
          <Checks label="Class spell lists" options={[...new Set(lists)]} value={f.lists} onChange={(v) => set({ lists: v })} hint="Characters of these classes can choose it." />
          <Field label="What it does" hint="Plain text. Leave a blank line between paragraphs. Write damage as dice (2d8 fire damage) so the simulator reads it, or say it under What it rolls." wide>
            <textarea rows={7} className={area} value={f.description} onChange={(e) => set({ description: e.target.value })} maxLength={20000} />
          </Field>
          <Field label="At higher levels (optional)" wide>
            <textarea rows={2} className={area} value={f.higherLevels ?? ""} onChange={(e) => set({ higherLevels: e.target.value || null })} maxLength={5000} />
          </Field>
          <SpellMechanicsFields f={f} set={set} />
        </div>
        <ReviewFooter kind="spells" model={model} busy={busy} ready={!!f.name.trim()} onClose={onClose} onSave={onSave} />
      </DialogContent>
    </Dialog>
  );
}

/** What a spell rolls, said outright: the battle view reads these instead of its text, so they never apply twice. */
function SpellMechanicsFields({ f, set }: { f: HomebrewSpell; set: (patch: Partial<HomebrewSpell>) => void }) {
  return (
    <fieldset className="grid gap-3 rounded-md border p-3 sm:col-span-2 sm:grid-cols-2">
      <legend className="px-1 text-sm font-medium">What it rolls (optional)</legend>
      <p className="text-xs text-muted-foreground sm:col-span-2">Said here, the battle view and its &ldquo;Why?&rdquo; use these numbers and no longer read them from the text.</p>
      <Field label="Roll">
        <select className={select} value={f.roll ?? ""} onChange={(e) => set({ roll: (e.target.value || null) as SpellRoll | null })}>
          {ROLLS.map((r) => (
            <option key={r.value} value={r.value}>
              {r.label}
            </option>
          ))}
        </select>
      </Field>
      {f.roll === "save" && (
        <Field label="Saving throw">
          <select className={select} value={f.saveAbility ?? ""} onChange={(e) => set({ saveAbility: e.target.value || null })}>
            <option value="">Choose an ability</option>
            {ABILITIES.map((a) => (
              <option key={a}>{a}</option>
            ))}
          </select>
        </Field>
      )}
      {f.roll && (
        <>
          <Field label="Damage" hint='Dice and type, e.g. "2d6 fire"; several with ";", e.g. "2d6 fire; 1d6 radiant"'>
            <Input value={f.damage ?? ""} onChange={(e) => set({ damage: e.target.value || null })} maxLength={200} />
          </Field>
          <Field label="Healing" hint='Dice, e.g. "2d8"'>
            <Input value={f.healing ?? ""} onChange={(e) => set({ healing: e.target.value || null })} maxLength={50} />
          </Field>
          {f.level === 0 ? (
            <Field label="Cantrip dice by character level" hint='The first damage (or the healing) from a level on, e.g. "5:2d10; 11:3d10; 17:4d10"'>
              <Input value={f.cantripScaling ?? ""} onChange={(e) => set({ cantripScaling: e.target.value || null })} maxLength={200} />
            </Field>
          ) : (
            <Field label="Dice for each slot level above" hint={`Added to the first damage (or the healing) for each slot level above ${ordinal(f.level)}, e.g. "1d6"`}>
              <Input value={f.slotScaling ?? ""} onChange={(e) => set({ slotScaling: e.target.value || null })} maxLength={50} />
            </Field>
          )}
          <Checks
            label="Also"
            options={[...(f.roll === "save" ? ["Half damage on a successful save"] : []), "Adds the spellcasting modifier"]}
            value={[f.roll === "save" && f.halfOnSave && "Half damage on a successful save", f.addsModifier && "Adds the spellcasting modifier"].filter((x): x is string => !!x)}
            onChange={(v) => set({ halfOnSave: v.includes("Half damage on a successful save"), addsModifier: v.includes("Adds the spellcasting modifier") })}
            hint="The modifier joins the first damage, or the healing."
          />
        </>
      )}
    </fieldset>
  );
}

// ---- subclasses ----

export function HomebrewSubclasses({ startNew }: { startNew?: boolean }) {
  const { data, isLoading } = useHomebrewSubclasses();
  const targets = useSubclassTargets();
  const { saveSubclass } = useHomebrewActions();
    const fresh = (): HomebrewSubclass => ({ name: "", source: "Homebrew", classSupports: "", className: "", description: "", features: [{ level: 3, name: "", description: "" }] });
// a tab mounts when it is shown: ?new=1 opens its form once
  const [editing, setEditing] = useState<HomebrewSubclass | null>(() => (startNew && isAdmin() ? fresh() : null));
  const subclasses = data ?? [];
  return (
    <div className="space-y-4">
      <Header
        text="A subclass made here appears in its class's subclass choice (with Homebrew ticked in the character's Sources), and its features arrive at their levels."
        newLabel="New subclass"
        onNew={() => setEditing(fresh())}
      />
      {isLoading && <Spinner className="mx-auto my-8 size-6" />}
      {!isLoading && subclasses.length === 0 && <p className="celestial-empty text-sm text-muted-foreground">No homebrew subclasses yet.</p>}
      <div className="grid gap-3 md:grid-cols-2">
        {subclasses.map((s) => (
          <article key={s.id} className="rounded-lg border bg-background/60 p-3">
            <div className="flex flex-wrap items-center gap-2">
              <h3 className="font-medium">{s.name}</h3>
              <Badge variant="outline">{s.className || s.classSupports}</Badge>
              <span className="flex-1" />
              <RowActions name={s.name} kind="subclasses" id={s.id!} warn="Characters who chose it lose it and its features." onEdit={() => setEditing(s)} />
            </div>
            <ul className="mt-1 space-y-0.5 text-sm text-muted-foreground">
              {s.features.map((f) => (
                <li key={f.id ?? f.name}>
                  Level {f.level}: {f.name}
                </li>
              ))}
            </ul>
          </article>
        ))}
      </div>
      {editing && (
        <Dialog open onOpenChange={(o) => !o && setEditing(null)}>
          <SubclassDialog subclass={editing} targets={targets.data ?? []} busy={saveSubclass.isPending} onClose={() => setEditing(null)} onSave={(s) => saveSubclass.mutateAsync(s).then(saved(s.name, () => setEditing(null)))} />
        </Dialog>
      )}
    </div>
  );
}

function SubclassDialog({
  subclass,
  targets,
  busy,
  onClose,
  onSave,
}: {
  subclass: HomebrewSubclass;
  targets: { className: string; source?: string | null; supports: string }[];
  busy: boolean;
  onClose: () => void;
  onSave: (s: HomebrewSubclass) => Promise<unknown>;
}) {
  const [f, setF] = useState<HomebrewSubclass>(subclass);
  const set = (patch: Partial<HomebrewSubclass>) => setF((x) => ({ ...x, ...patch }));
  const label = (t: { className: string; source?: string | null }) => `${t.className}${t.source ? ` (${t.source})` : ""}`;
  return (
    <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-2xl">
      <DialogHeader>
        <DialogTitle>{subclass.id ? `Edit ${subclass.name}` : "New subclass"}</DialogTitle>
        <DialogDescription>Pick the class whose subclass choice it joins; its features arrive at the levels you give them.</DialogDescription>
      </DialogHeader>
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label="Name">
          <Input value={f.name} onChange={(e) => set({ name: e.target.value })} maxLength={100} autoFocus />
        </Field>
        <Field label="Book">
          <Input value={f.source ?? ""} onChange={(e) => set({ source: e.target.value })} maxLength={100} />
        </Field>
        <Field label="Class" hint="2014 and 2024 versions of a class have separate subclass choices." wide>
          <select
            className={select}
            value={f.classSupports}
            onChange={(e) => {
              const t = targets.find((x) => x.supports === e.target.value);
              set({ classSupports: e.target.value, className: t ? label(t) : "" });
            }}
          >
            <option value="">Choose the class…</option>
            {targets.map((t) => (
              <option key={`${t.className}|${t.source}|${t.supports}`} value={t.supports}>
                {label(t)}
              </option>
            ))}
          </select>
        </Field>
        <Field label="Description" wide>
          <textarea rows={3} className={area} value={f.description} onChange={(e) => set({ description: e.target.value })} maxLength={20000} />
        </Field>
        <Features value={f.features} onChange={(features) => set({ features })} />
      </div>
      <ReviewFooter kind="subclasses" model={f} busy={busy} ready={!!f.name.trim() && !!f.classSupports && f.features.every((x) => x.name.trim())} onClose={onClose} onSave={onSave} />
    </DialogContent>
  );
}

// ---- classes ----

const NEW_CLASS: HomebrewClass = {
  name: "", source: "Homebrew", description: "", hitDie: 8, savingThrows: [], armor: ["Light"], weapons: ["Simple"], skillChoices: 2, skills: [],
  multiclassArmor: ["Light"], multiclassWeapons: [], multiclassAbility: null, subclassLevel: 3, asiLevels: [4, 8, 12, 16], epicBoon: true,
  features: [{ level: 1, name: "", description: "" }], casting: "None", castingAbility: null, spellLists: [], cantrips: 3,
};

export function HomebrewClasses({ startNew }: { startNew?: boolean }) {
  const { data, isLoading } = useHomebrewClasses();
  const { saveClass } = useHomebrewActions();
  // a tab mounts when it is shown: ?new=1 opens its form once
  const [editing, setEditing] = useState<HomebrewClass | null>(() => (startNew && isAdmin() ? { ...NEW_CLASS } : null));
  const classes = data ?? [];
  return (
    <div className="space-y-4">
      <Header
        text="A class made here is offered in the builder like the books' classes (with Homebrew ticked in the character's Sources), with multiclassing, its subclass choice, Ability Score Improvements and, if it casts, the 2024 spellcasting tables."
        newLabel="New class"
        onNew={() => setEditing({ ...NEW_CLASS })}
      />
      {isLoading && <Spinner className="mx-auto my-8 size-6" />}
      {!isLoading && classes.length === 0 && <p className="celestial-empty text-sm text-muted-foreground">No homebrew classes yet.</p>}
      <div className="grid gap-3 md:grid-cols-2">
        {classes.map((c) => (
          <article key={c.id} className="rounded-lg border bg-background/60 p-3">
            <div className="flex flex-wrap items-center gap-2">
              <h3 className="font-medium">{c.name}</h3>
              <Badge variant="outline">d{c.hitDie}</Badge>
              <span className="text-xs text-muted-foreground">
                {c.casting === "None" ? "no spellcasting" : `${c.casting} caster (${c.castingAbility})`} · subclass at level {c.subclassLevel}
              </span>
              <span className="flex-1" />
              <RowActions name={c.name} kind="classes" id={c.id!} warn="Characters with levels in it lose them, and its subclasses lose their class." onEdit={() => setEditing(c)} />
            </div>
            <ul className="mt-1 space-y-0.5 text-sm text-muted-foreground">
              {c.features.map((f) => (
                <li key={f.id ?? f.name}>
                  Level {f.level}: {f.name}
                </li>
              ))}
            </ul>
          </article>
        ))}
      </div>
      {editing && <ClassDialog cls={editing} busy={saveClass.isPending} onClose={() => setEditing(null)} onSave={(c) => saveClass.mutateAsync(c).then(saved(c.name, () => setEditing(null)))} />}
    </div>
  );
}

function ClassDialog({ cls, busy, onClose, onSave }: { cls: HomebrewClass; busy: boolean; onClose: () => void; onSave: (c: HomebrewClass) => Promise<unknown> }) {
  const [f, setF] = useState<HomebrewClass>(cls);
  const set = (patch: Partial<HomebrewClass>) => setF((x) => ({ ...x, ...patch }));
  const [asiText, setAsiText] = useState(cls.asiLevels.join(", "));
  const nameOk = /^[\p{L}0-9 '’()-]{1,50}$/u.test(f.name.trim());
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>{cls.id ? `Edit ${cls.name}` : "New class"}</DialogTitle>
          <DialogDescription>Built the way the 2024 Player's Handbook builds its classes. Its subclasses are made on the Subclasses tab.</DialogDescription>
        </DialogHeader>
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="Name" hint={f.name && !nameOk ? "Letters, numbers, spaces and ' ( ) - only." : undefined}>
            <Input value={f.name} onChange={(e) => set({ name: e.target.value })} maxLength={50} autoFocus />
          </Field>
          <Field label="Book">
            <Input value={f.source ?? ""} onChange={(e) => set({ source: e.target.value })} maxLength={100} />
          </Field>
          <Field label="Hit die">
            <select className={select} value={f.hitDie} onChange={(e) => set({ hitDie: Number(e.target.value) })}>
              {[6, 8, 10, 12].map((d) => (
                <option key={d} value={d}>
                  d{d}
                </option>
              ))}
            </select>
          </Field>
          <Field label="Subclass at level">
            <Input inputMode="numeric" value={f.subclassLevel} onChange={(e) => set({ subclassLevel: Math.min(20, Math.max(1, Number(e.target.value.replace(/\D/g, "")) || 1)) })} />
          </Field>
          <Field label="Description" wide>
            <textarea rows={3} className={area} value={f.description} onChange={(e) => set({ description: e.target.value })} maxLength={20000} />
          </Field>
          <Checks label="Saving throws" options={ABILITIES} value={f.savingThrows} onChange={(savingThrows) => set({ savingThrows })} hint="Usually two." />
          <Checks label="Armor training" options={ARMOR} value={f.armor} onChange={(armor) => set({ armor, multiclassArmor: f.multiclassArmor.filter((a) => armor.includes(a)) })} />
          <Checks label="Weapon proficiencies" options={WEAPONS} value={f.weapons} onChange={(weapons) => set({ weapons, multiclassWeapons: f.multiclassWeapons.filter((w) => weapons.includes(w)) })} />
          <Checks label="Skills to choose from" options={SKILLS} value={f.skills} onChange={(skills) => set({ skills })} />
          <Field label="How many skills a character chooses">
            <Input inputMode="numeric" value={f.skillChoices} onChange={(e) => set({ skillChoices: Math.min(6, Math.max(0, Number(e.target.value.replace(/\D/g, "")) || 0)) })} />
          </Field>
          <Field label="Multiclassing needs 13 in">
            <select className={select} value={f.multiclassAbility ?? ""} onChange={(e) => set({ multiclassAbility: e.target.value || null })}>
              <option value="">No ability</option>
              {ABILITIES.map((a) => (
                <option key={a}>{a}</option>
              ))}
            </select>
          </Field>
          <Checks label="A multiclassing character gets armor" options={f.armor} value={f.multiclassArmor} onChange={(multiclassArmor) => set({ multiclassArmor })} />
          <Checks label="…and weapons" options={f.weapons} value={f.multiclassWeapons} onChange={(multiclassWeapons) => set({ multiclassWeapons })} hint="Plus one skill from its list. Saving throws come only with the first class." />
          <Field label="Ability Score Improvement levels" hint="A feat at each of these levels (2024 rules)">
            <Input
              value={asiText}
              onChange={(e) => {
                setAsiText(e.target.value);
                set({ asiLevels: [...new Set(e.target.value.split(/[^\d]+/).map(Number).filter((n) => n >= 1 && n <= 20))] });
              }}
              placeholder="4, 8, 12, 16"
            />
          </Field>
          <Field label="Epic Boon at level 19">
            <select className={select} value={f.epicBoon ? "yes" : "no"} onChange={(e) => set({ epicBoon: e.target.value === "yes" })}>
              <option value="yes">Yes</option>
              <option value="no">No</option>
            </select>
          </Field>
          <Field label="Spellcasting" wide>
            <select className={select} value={f.casting} onChange={(e) => set({ casting: e.target.value, castingAbility: f.castingAbility ?? "Intelligence", cantrips: e.target.value === "Pact" ? 2 : 3 })}>
              {CASTING.map((c) => (
                <option key={c.value} value={c.value}>
                  {c.label}
                </option>
              ))}
            </select>
          </Field>
          {f.casting !== "None" && (
            <>
              <Field label="Spellcasting ability">
                <select className={select} value={f.castingAbility ?? "Intelligence"} onChange={(e) => set({ castingAbility: e.target.value })}>
                  {ABILITIES.map((a) => (
                    <option key={a}>{a}</option>
                  ))}
                </select>
              </Field>
              {f.casting !== "Half" ? (
                <Field label="Cantrips at level 1" hint="One more at levels 4 and 10.">
                  <Input inputMode="numeric" value={f.cantrips} onChange={(e) => set({ cantrips: Math.min(6, Math.max(0, Number(e.target.value.replace(/\D/g, "")) || 0)) })} />
                </Field>
              ) : (
                <span />
              )}
              <Checks
                label="Also casts from these lists"
                options={BOOK_LISTS}
                value={f.spellLists}
                onChange={(spellLists) => set({ spellLists })}
                hint={`Its own list (${f.spellListName ?? (f.name || "the class's name")}) starts empty: tick a book's list, or add homebrew spells to it on the Spells tab.`}
              />
            </>
          )}
          <Features value={f.features} onChange={(features) => set({ features })} />
        </div>
        <ReviewFooter
          kind="classes"
          model={f}
          busy={busy}
          ready={nameOk && f.features.every((x) => x.name.trim()) && (f.casting === "None" || f.spellLists.length > 0 || !!cls.id)}
          onClose={onClose}
          onSave={onSave}
        />
      </DialogContent>
    </Dialog>
  );
}
