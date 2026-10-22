import { useState, type ReactNode } from "react";
import { Link, useParams } from "react-router-dom";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { RichText } from "@/components/rich-text";
import { WhyValue } from "@/components/why-value";
import { Spinner } from "@/components/ui/spinner";
import { useSheetData } from "@/lib/api/sheet";
import { useCombat, useSaveCombat } from "@/lib/api/combat";
import { useMagic, useSaveMagic, useSpellcasting } from "@/lib/api/magic";
import { buildBattleModel } from "@/lib/battle-model";
import {
  CONDITIONS,
  currentHitPoints,
  effectFor,
  effectiveArmorClass,
  effectiveSpeed,
  exhaustionEffect,
  gainTemporary,
  heal,
  maxHitPoints,
  signed,
  takeDamage,
  type BattleEffect,
  type CombatState,
} from "@/lib/rules/battle";
import { pactSlots } from "@/lib/rules/magic";
import { slugOf } from "@/lib/rules/trace";
import { Vitals, Status, RestDialog } from "@/pages/battle/battle-page";
import { MagicTab } from "../builder/magic-tab";
import { EquipmentTab } from "../builder/equipment-tab";

import { useUnits } from "@/components/units-provider";

// saving-throw keys name the ability ("saving-throw:dexterity")
const ABILITY_NAME: Record<string, string> = { STR: "strength", DEX: "dexterity", CON: "constitution", INT: "intelligence", WIS: "wisdom", CHA: "charisma" };
const toHitOf = (attack: string) => {
  const n = attack.replace(/−/g, "-").match(/[+-]?\d+/)?.[0];
  return n === undefined ? null : Number(n);
};
function Section({ id, title, children, open, onToggle }: { id: string; title: string; children: ReactNode; open: boolean; onToggle: (open: boolean) => void }) {
  return (
    <section id={id} className="surface p-4 sm:p-5">
      <details open={open} onToggle={(event) => onToggle(event.currentTarget.open)}>
        <summary className="section-heading py-2">{title}</summary>
        <div className="mt-4">{children}</div>
      </details>
    </section>
  );
}

/** Same character, inventory, magic and combat state as the builder and simulator; PDF remains a separate route. */
export function InteractiveSheetPage() {
  const u = useUnits();
  const { id = "" } = useParams();
  const sheet = useSheetData(id);
  const combat = useCombat(id);
  const magic = useMagic(id);
  const casting = useSpellcasting(id);
  const saveCombat = useSaveCombat(id);
  const saveMagic = useSaveMagic(id);
  // Keep navigation state above loading guards: preparing spells can refresh dependent sheet queries.
  const [expanded, setExpanded] = useState<Record<string, boolean>>({ abilities: true, skills: true, attacks: true, resources: true });
  const sectionProps = (sectionId: string) => ({
    open: expanded[sectionId] ?? false,
    onToggle: (open: boolean) => setExpanded((previous) => previous[sectionId] === open ? previous : { ...previous, [sectionId]: open }),
  });
  const [rest, setRest] = useState<"short" | "long" | null>(null);
  if (sheet.error || combat.error || magic.error || casting.error)
    return (
      <div role="alert" className="mx-auto max-w-lg space-y-4 p-8">
        <h1 className="text-xl font-semibold">Character unavailable</h1>
        <p>Check that this character's player is unlocked, then try again.</p>
        <Button asChild>
          <Link to="/characters">Back to characters</Link>
        </Button>
      </div>
    );
  if (!sheet.data || !combat.data || !magic.data || !casting.data)
    return (
      <div role="status" className="p-12 text-center">
        <Spinner className="mx-auto" />
        Loading character…
      </div>
    );
  const data = sheet.data;
  const explain = sheet.explain;
  const state = combat.data;
  const model = buildBattleModel(data);
  const effectCtx = { edition: model.edition, mod: model.mod, proficiencyBonus: data.proficiencyBonus, rageDamage: data.rageDamage ?? 0 };
  const effects = (state.effects ?? []).map((key) => effectFor(key, effectCtx)).filter((e): e is BattleEffect => !!e);
  const ac = effectiveArmorClass(
    { total: data.armorClass, base: data.armor.base ?? data.armorClass, armorKind: data.armor.armorKind, shield: data.armor.hasShield },
    effects,
  );
  const maxHp = maxHitPoints(data.hitPoints ?? 0, state, model.edition) + effects.reduce((n, e) => n + (e.maxHpBonus ?? 0), 0);
  const hp = currentHitPoints(maxHp, state);
  const exhaustion = exhaustionEffect(state.exhaustion, model.edition);
  const speed =
    exhaustion.speedZero || CONDITIONS.some((c) => state.conditions.includes(c.name) && c.speedZero)
      ? 0
      : effectiveSpeed(Math.max(0, (exhaustion.speedHalved ? Math.floor(data.speeds.walk / 2) : data.speeds.walk) - exhaustion.speedPenalty), effects);
  // what play adds to the sheet's figures; "Why this value?" explains the sheet's figure and names these
  const hpChanges = [
    ...effects.filter((e) => e.maxHpBonus).map((e) => `${e.name} ${signed(e.maxHpBonus ?? 0)}`),
    ...(maxHitPoints(data.hitPoints ?? 0, state, model.edition) !== (data.hitPoints ?? 0) ? [`exhaustion ${state.exhaustion}`] : []),
  ];
  const speedChanges = [
    ...effects.filter((e) => e.speedBonus || e.speedMultiplier).map((e) => e.name),
    ...(state.exhaustion > 0 && (exhaustion.speedHalved || exhaustion.speedPenalty || exhaustion.speedZero) ? [`exhaustion ${state.exhaustion}`] : []),
    ...CONDITIONS.filter((c) => state.conditions.includes(c.name) && c.speedZero).map((c) => c.name),
  ];
  const why = (traceKey: string, label: string, shown: number | null, changes?: string[]) => (
    <WhyValue characterId={id} traceKey={traceKey} label={label} explain={explain} shown={shown} effects={changes} />
  );
  const failed = (e: Error) => toast.error("Could not save character state", { description: e.message });
  const updateCombat = (change: (s: CombatState) => CombatState) => saveCombat.update(change, failed);
  const ctx = {
    data,
    model,
    state,
    effectCtx,
    pact: pactSlots(casting.data.casters),
    updateCombat,
    updateMagic: (change: Parameters<typeof saveMagic.update>[0]) => saveMagic.update(change, failed),
  };
  const busy = saveCombat.isPending || saveMagic.isPending;
  const sections = ["Abilities", "Skills", "Attacks", "Spellcasting", "Inventory", "Features", "Resources"];
  return (
    <div className="character-sheet mx-auto max-w-7xl space-y-5 px-3 py-6 sm:px-6 3xl:max-w-[110rem]">
      <header data-ornament="corner" className="surface flex flex-wrap items-center gap-4 p-4 sm:p-6">
        {data.portraitUrl && <img src={data.portraitUrl} alt="" className="size-20 rounded-xl object-cover" />}
        <div className="min-w-0 flex-1 basis-48">
          <p className="text-sm text-muted-foreground">Character sheet · {data.edition} rules</p>
          <h1 className="break-words text-3xl font-semibold">{data.name}</h1>
          <p>{data.classLine}</p>
          <p className="text-sm text-muted-foreground">{[data.background, data.alignment, data.player].filter(Boolean).join(" · ")}</p>
        </div>
        <nav aria-label="Character views" className="flex flex-wrap gap-2">
          <Button variant="outline" asChild>
            <Link to={`/characters/${id}`}>Builder</Link>
          </Button>
          <Button variant="outline" asChild>
            <Link to={`/battle/${id}`}>Battle</Link>
          </Button>
          <Button variant="outline" asChild>
            <Link to={`/characters/${id}/sheet`}>Aurora PDF</Link>
          </Button>
        </nav>
      </header>
      <nav aria-label="Sheet sections" className="sheet-navigation grid grid-cols-3 gap-2 sm:grid-cols-4 lg:grid-cols-7">
        {sections.map((name) => (
          <a key={name} href={`#${name.toLowerCase()}`} onClick={() => {
            setExpanded((previous) => ({ ...previous, [name.toLowerCase()]: true }));
          }}>
            {name}
          </a>
        ))}
      </nav>
      <p className="text-sm text-muted-foreground" role="status">
        {busy ? "Saving changes…" : saveCombat.isError || saveMagic.isError
          ? "A change could not be saved. Check your connection and try again."
          : "Play state is shared with the battle simulator. Changes save automatically."}
      </p>
      <fieldset disabled={busy} className="min-w-0 space-y-5">
        <legend className="sr-only">Character play state</legend>
        <Vitals
          data={data}
          model={model}
          state={state}
          hp={hp}
          maxHp={maxHp}
          ac={ac}
          speed={speed}
          onDamage={(n) => {
            updateCombat((c) => takeDamage(c, n, maxHp));
            if (state.concentration) toast("Check concentration", { description: "Use the battle simulator to resolve the concentration save." });
          }}
          onHeal={(n) => updateCombat((c) => heal(c, n))}
          onTemp={(n) => updateCombat((c) => gainTemporary(c, n))}
        />
        <p className="flex flex-wrap items-center gap-x-4 gap-y-1 text-sm text-muted-foreground">
          <span>Why these values:</span>
          <span className="inline-flex items-center">Armor class {why("armor-class", "Armor class", ac.total, ac.changes)}</span>
          <span className="inline-flex items-center">Hit point maximum {why("hit-points", "Hit point maximum", maxHp, hpChanges)}</span>
          <span className="inline-flex items-center">Speed {why("speed:walk", "Walking speed", speed, speedChanges)}</span>
        </p>
        <Status data={data} state={state} edition={data.edition} exhaustionText={exhaustion.text} updateCombat={updateCombat} hp={hp} />
        {effects.length > 0 && (
          <p className="rounded-lg bg-muted p-3 text-sm">
            Active effects: {effects.map((e) => e.name).join(", ")}.{" "}
            <Link className="underline" to={`/battle/${id}`}>
              Manage in battle
            </Link>
          </p>
        )}
        <div className="grid items-start gap-5 lg:grid-cols-2">
          <Section {...sectionProps("abilities")} id="abilities" title="Abilities & saving throws">
            <p className="mb-3 text-sm">
              Proficiency bonus <strong>{signed(data.proficiencyBonus)}</strong>
              {why("proficiency-bonus", "Proficiency bonus", data.proficiencyBonus)} · Initiative <strong>{signed(data.initiative)}</strong>
              {why("initiative", "Initiative", data.initiative)}
            </p>
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
              {data.abilities.map((a) => (
                <div key={a.abilityScoreId} className="rounded-lg bg-muted p-3 text-center">
                  <p className="text-sm">{a.name}</p>
                  <p className="text-2xl font-semibold">
                    {signed(a.calculatedModifier)}
                    {why(`ability-modifier:${slugOf(a.name)}`, `${a.name} modifier`, a.calculatedModifier)}
                  </p>
                  <p className="text-sm text-muted-foreground">Score {a.calculatedScore}</p>
                </div>
              ))}
            </div>
            <dl className="mt-4 grid gap-x-6 gap-y-2 sm:grid-cols-2">
              {data.saves.map((s) => (
                <div key={s.name} className="flex justify-between gap-2 text-sm">
                  <dt>
                    {s.name}
                    {s.proficiency !== "none" ? " · Proficient" : ""}
                  </dt>
                  <dd className="font-semibold">
                    {signed(s.calculatedBonus)}
                    {why(`saving-throw:${ABILITY_NAME[s.abilityScoreAbbreviation] ?? slugOf(s.name)}`, /saving throw/i.test(s.name) ? s.name : `${s.name} saving throw`, s.calculatedBonus)}
                  </dd>
                </div>
              ))}
            </dl>
            <p className="mt-3 text-sm text-muted-foreground">Saving throw bonuses before temporary situational effects.</p>
          </Section>
          <Section {...sectionProps("skills")} id="skills" title="Skills & senses">
            <dl className="grid gap-x-6 gap-y-3 sm:grid-cols-2">
              {data.skills.map((s) => (
                <div key={s.name} className="flex items-start justify-between gap-2 text-sm">
                  <dt>
                    {s.name}
                    <span className="block text-muted-foreground">
                      {s.abilityScoreAbbreviation}
                      {s.proficiency !== "none" ? ` · ${s.proficiency}` : ""}
                    </span>
                  </dt>
                  <dd className="font-semibold">
                    {signed(s.calculatedBonus)}
                    {why(`skill:${slugOf(s.name)}`, s.name, s.calculatedBonus)}
                  </dd>
                </div>
              ))}
            </dl>
            <p className="mt-4 text-sm">
              Passive Perception {data.passivePerception}
              {why("passive-perception", "Passive Perception", data.passivePerception)} · {data.vision.join(", ") || "Normal vision"}
            </p>
            <p className="mt-2 text-sm text-muted-foreground">Languages: {data.languages.join(", ") || "None selected"}</p>
          </Section>
        </div>
        <Section {...sectionProps("attacks")} id="attacks" title="Attacks">
          <div className="grid gap-3 sm:grid-cols-2">
            {data.attacks.map((a, i) => (
              <details key={`${a.name}:${i}`} className="rounded-lg bg-muted p-3">
                <summary className="py-1 font-semibold">
                  {a.name}
                  <span className="block text-sm font-normal">
                    {a.attack} · {a.damage}
                  </span>
                </summary>
                <p className="mt-2 text-sm">{u.range(a.range)}</p>
                {a.traceId && (
                  <p className="text-sm text-muted-foreground">
                    Attack bonus {why(`attack:${a.traceId}:to-hit`, `${a.name} attack bonus`, toHitOf(a.attack))} · Damage
                    bonus {why(`attack:${a.traceId}:damage`, `${a.name} damage bonus`, null)}
                  </p>
                )}
                <RichText text={a.description} />
              </details>
            ))}
          </div>
          {!data.attacks.length && <p>No weapon attacks yet. Equip a weapon in Inventory.</p>}
          <p className="mt-3 text-sm text-muted-foreground">
            Base attack values.{" "}
            <Link className="underline" to={`/battle/${id}`}>
              Use the simulator
            </Link>{" "}
            for rolls, conditions, targets and action economy.
          </p>
        </Section>
        <Section {...sectionProps("spellcasting")} id="spellcasting" title="Spellcasting">
          {data.spellcasting.length > 0 && (
            <p className="mb-3 text-sm">
              {data.spellcasting.map((c, i) => (
                <span key={c.name} className="inline-flex flex-wrap items-center">
                  {i > 0 && <span className="mx-2">·</span>}
                  {c.name}: spell attack <strong className="ml-1">{signed(c.attackBonus)}</strong>
                  {why(`spell-attack:${slugOf(c.name)}`, `${c.name} spell attack`, c.attackBonus)}, save DC <strong className="ml-1">{c.saveDc}</strong>
                  {why(`spell-save-dc:${slugOf(c.name)}`, `${c.name} spell save DC`, c.saveDc)}
                </span>
              ))}
            </p>
          )}
          <MagicTab characterId={id} />
        </Section>
        <Section {...sectionProps("inventory")} id="inventory" title="Inventory">
          <EquipmentTab characterId={id} />
        </Section>
        <Section {...sectionProps("features")} id="features" title="Features & traits">
          <div className="grid items-start gap-3 sm:grid-cols-2">
            {[...data.speciesTraits, ...data.features, ...(data.backgroundFeature ? [data.backgroundFeature] : [])].map((f, i) => (
              <details key={`${f.title}:${i}`} className="rounded-lg bg-muted p-3">
                <summary className="py-1 font-medium">{f.title}</summary>
                <p className="my-2 text-sm text-muted-foreground">{[f.action, f.usage].filter(Boolean).join(" · ")}</p>
                <RichText text={f.text} />
              </details>
            ))}
          </div>
          <p className="mt-4 text-sm">
            Armor: {data.proficiencies.armor.join(", ") || "None"} · Weapons: {data.proficiencies.weapons.join(", ") || "None"} · Tools:{" "}
            {data.proficiencies.tools.join(", ") || "None"}
          </p>
          {Object.entries(data.defenses).map(([label, entries]) =>
            entries.length ? (
              <p key={label} className="mt-2 text-sm capitalize">
                {label}: {entries.join(", ")}
              </p>
            ) : null,
          )}
        </Section>
        <Section {...sectionProps("resources")} id="resources" title="Resources & rests">
          <div className="flex flex-wrap gap-2">
            <Button variant="outline" onClick={() => setRest("short")}>
              Short rest
            </Button>
            <Button onClick={() => setRest("long")}>Long rest</Button>
          </div>
          <p className="my-3 text-sm text-muted-foreground">Rest controls use the simulator's existing rules, including multiclass hit dice.</p>
          <div className="grid gap-3 sm:grid-cols-2">
            {model.features
              .filter((f) => f.parsedUsage?.kind === "count")
              .map((f) => {
                const usage = f.parsedUsage;
                if (usage?.kind !== "count") return null;
                const spent = state.uses[f.title] ?? 0;
                return (
                  <div key={f.title} className="rounded-lg bg-muted p-3">
                    <p className="font-medium">{f.title}</p>
                    <p className="text-sm">
                      {Math.max(0, usage.max - spent)} / {usage.max} remaining · {f.usage}
                    </p>
                  </div>
                );
              })}
          </div>
          <Button variant="link" asChild>
            <Link to={`/battle/${id}`}>Use features and resources in battle</Link>
          </Button>
        </Section>
      </fieldset>
      <RestDialog kind={rest} onClose={() => setRest(null)} ctx={ctx} totalHitDice={data.level} />
    </div>
  );
}
