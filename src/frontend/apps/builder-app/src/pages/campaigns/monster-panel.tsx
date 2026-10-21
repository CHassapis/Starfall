/**
 * A monster's actions for the DM in encounter mode: each attack with its chance to hit the chosen party member and a
 * button that rolls it (and its damage on a hit), each saving-throw ability with its DC and its damage rolled once, and
 * what limits them: uses per day, a recharge (rolled at the start of its turn), legendary actions per round. The stat
 * block's text says what each does; its spells are listed with their DC.
 */
import { DicesIcon, RotateCcwIcon, ZapIcon } from "lucide-react";
import { useMemo, useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { useLoreEntry, useLoreMeta } from "@/lib/lore/data";
import { hitChance, percent, signed, type Advantage } from "@/lib/rules/battle";
import type { Combatant } from "@/lib/rules/encounter";
import type { HomebrewMonster } from "@/lib/rules/homebrew";
import {
  canUse,
  homebrewKit,
  monsterKit,
  rollAttack,
  rollSaveDamage,
  SECTION_TITLES,
  spendAction,
  type ActionSection,
  type MonsterAction,
  type MonsterKit,
} from "@/lib/rules/monster-actions";
import { cn } from "@/lib/utils";

import { useUnits } from "@/components/units-provider";
/** Loads the monster's stat block (once opened) and shows its actions. */
export function MonsterPanel({ c, homebrew, party, update }: { c: Combatant; homebrew?: HomebrewMonster; party: Combatant[]; update: (patch: Partial<Combatant>) => void }) {
  const meta = useLoreMeta();
  const key = c.link?.category === "bestiary" ? c.link.key : null;
  const entry = useLoreEntry(meta.data, "bestiary", key);
  const kit = useMemo(() => (homebrew ? homebrewKit(homebrew) : entry.data?.entry ? monsterKit(entry.data.entry as Record<string, unknown>) : null), [homebrew, entry.data]);
  if (!homebrew && key && (meta.isLoading || entry.isLoading)) return <Spinner className="my-2 size-4" />;
  if (!kit || (kit.actions.length === 0 && kit.spellcasting.length === 0)) return <p className="text-xs text-muted-foreground">No actions to run: this creature has no stat block in the Compendium or the Homebrew page.</p>;
  return <Actions c={c} kit={kit} party={party} update={update} />;
}

const ORDER: ActionSection[] = ["action", "bonus", "reaction", "legendary", "mythic", "trait"];

function Actions({ c, kit, party, update }: { c: Combatant; kit: MonsterKit; party: Combatant[]; update: (patch: Partial<Combatant>) => void }) {
  const targets = party.filter((p) => p.kind === "pc");
  // the first party member whose armor class is known (from their simulator)
  const [targetId, setTargetId] = useState<string>((targets.find((t) => t.ac != null) ?? targets[0])?.id ?? "");
  const [ownAc, setOwnAc] = useState("15");
  const [advantage, setAdvantage] = useState<Advantage>("normal");
  const [results, setResults] = useState<Record<string, string>>({});
  const target = targets.find((t) => t.id === targetId);
  const ac = target?.ac ?? (Number(ownAc) || 10);
  const state = { used: c.used, recharging: c.recharging, legendarySpent: c.legendarySpent };
  const spend = (a: MonsterAction) => {
    const next = spendAction(a, state);
    update({ used: next.used, recharging: next.recharging, legendarySpent: next.legendarySpent });
  };
  const say = (a: MonsterAction, text: string) => setResults((r) => ({ ...r, [`${a.section}:${a.name}`]: text }));

  function run(a: MonsterAction) {
    if (a.attack) {
      const r = rollAttack(a, ac, advantage)!;
      const d20 = r.rolls.length > 1 ? `d20 ${r.rolls.join(" & ")} → ${r.kept}` : `d20 ${r.kept}`;
      const dmg = r.damage.map((p) => `${p.amount} ${p.type || "damage"}${p.condition ? ` (${p.condition})` : ""}`).join(" + ");
      say(a, r.hit ? `${r.crit ? "Critical hit!" : "Hit"} ${d20} ${signed(a.attack.bonus)} = ${r.total} vs AC ${ac}: ${r.totalDamage} damage${dmg ? ` (${dmg})` : ""}` : `Miss: ${d20} ${signed(a.attack.bonus)} = ${r.total} vs AC ${ac}`);
    } else if (a.damage.length) {
      const r = rollSaveDamage(a);
      const parts = r.parts.map((p) => `${p.amount} ${p.type || "damage"}${p.condition ? ` (${p.condition})` : ""}`).join(" + ");
      say(a, a.save ? `${a.save.ability || "A"} save DC ${a.save.dc}: ${r.full} on a failure${a.save.halfOnSuccess ? `, ${r.half} on a success` : ""} (${parts})` : `${r.full} (${parts})`);
    } else {
      say(a, "Used.");
    }
    if (a.recharge || a.perDay || a.section === "legendary") spend(a);
  }

  const legendaryLeft = Math.max(0, kit.legendaryActions - (c.legendarySpent ?? 0));
  return (
    <div className="mt-2 space-y-3 rounded-md border bg-background/60 p-2.5 text-sm">
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-xs text-muted-foreground">Against</span>
        {targets.length > 0 ? (
          <select aria-label={`${c.name}'s target`} value={targetId} onChange={(e) => setTargetId(e.target.value)} className="h-9 rounded-md border bg-transparent px-2 text-sm">
            {targets.map((t) => (
              <option key={t.id} value={t.id}>
                {t.name}
                {t.ac != null ? ` (AC ${t.ac})` : " (AC unknown)"}
              </option>
            ))}
            <option value="">another AC…</option>
          </select>
        ) : null}
        {(!target || target.ac == null) && (
          <label className="flex items-center gap-1 text-xs" title={target ? `${target.name}'s armor class shows once their player opens the Battle Action Simulator` : undefined}>
            AC
            <input aria-label="Target armor class" inputMode="numeric" value={ownAc} onChange={(e) => setOwnAc(e.target.value.replace(/\D/g, ""))} className="h-9 w-14 rounded-md border bg-transparent text-center text-sm" />
          </label>
        )}
        <span className="inline-flex overflow-hidden rounded-md border text-xs" role="group" aria-label="Advantage">
          {(["disadvantage", "normal", "advantage"] as Advantage[]).map((a) => (
            <button key={a} type="button" aria-pressed={advantage === a} onClick={() => setAdvantage(a)} className={cn("px-2.5 py-2", advantage === a ? "bg-primary text-primary-foreground" : "hover:bg-muted")}>
              {a === "normal" ? "Normal" : a === "advantage" ? "Adv." : "Disadv."}
            </button>
          ))}
        </span>
        {kit.legendaryActions > 0 && (
          <span className="ms-auto inline-flex items-center gap-1 text-xs" title="Legendary actions left this round (back at the start of its turn)">
            Legendary
            {Array.from({ length: kit.legendaryActions }, (_, i) => (
              <span key={i} className={cn("size-3 rounded-full border border-amber-500", i < legendaryLeft && "bg-amber-500")} />
            ))}
          </span>
        )}
      </div>
      {c.turnNotes && c.turnNotes.length > 0 && <p className="text-xs text-emerald-700 dark:text-emerald-300">Start of its turn: {c.turnNotes.join("; ")}</p>}

      {ORDER.map((section) => {
        const list = kit.actions.filter((a) => a.section === section);
        if (list.length === 0) return null;
        return (
          <section key={section} className="space-y-1.5">
            <h4 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">{SECTION_TITLES[section]}</h4>
            {list.map((a) => (
              <ActionRow key={`${a.section}:${a.name}`} a={a} kit={kit} c={c} ac={ac} advantage={advantage} result={results[`${a.section}:${a.name}`]} onRun={() => run(a)} />
            ))}
          </section>
        );
      })}

      {kit.spellcasting.map((sc) => (
        <section key={sc.name} className="space-y-1">
          <h4 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
            {sc.name}
            {sc.dc ? ` · save DC ${sc.dc}` : ""}
            {sc.attack != null ? ` · ${signed(sc.attack)} to hit` : ""}
          </h4>
          {sc.groups.map((g) => {
            const used = c.used?.[`spells:${g.label}`] ?? 0;
            const max = g.perDay ?? g.slots;
            return (
              <div key={g.label} className="flex flex-wrap items-baseline gap-x-2 gap-y-1 text-xs">
                <span className="font-medium">{g.label}:</span>
                <span className="min-w-0 flex-1">{g.spells.join(", ")}</span>
                {max ? (
                  <Button
                    size="sm"
                    variant="outline"
                    className="h-8"
                    disabled={used >= max}
                    onClick={() => update({ used: { ...c.used, [`spells:${g.label}`]: used + 1 } })}
                    title={g.perDay ? "Each spell of this line has its own uses; this counts casts of the line" : "Spend a slot of this level"}
                  >
                    Cast · {max - used} left
                  </Button>
                ) : null}
              </div>
            );
          })}
        </section>
      ))}

      {(c.used || c.recharging || c.legendarySpent) && (
        <Button size="sm" variant="ghost" className="h-8 text-xs text-muted-foreground" onClick={() => update({ used: undefined, recharging: undefined, legendarySpent: undefined, turnNotes: undefined })}>
          <RotateCcwIcon /> Reset its uses (a new day)
        </Button>
      )}
    </div>
  );
}

function ActionRow({ a, kit, c, ac, advantage, result, onRun }: { a: MonsterAction; kit: MonsterKit; c: Combatant; ac: number; advantage: Advantage; result?: string; onRun: () => void }) {
  const u = useUnits();
  const [open, setOpen] = useState(false);
  const usable = canUse(a, { used: c.used, recharging: c.recharging, legendarySpent: c.legendarySpent }, kit.legendaryActions);
  const runnable = !!a.attack || a.damage.length > 0 || !!a.recharge || !!a.perDay || a.section === "legendary";
  const chance = a.attack ? hitChance(a.attack.bonus, ac, advantage).hit : null;
  return (
    <div className={cn("rounded-md border px-2.5 py-2", !usable.ok && "opacity-60")}>
      <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
        <button type="button" onClick={() => setOpen((o) => !o)} className="text-left font-medium underline decoration-dotted underline-offset-4" aria-expanded={open}>
          {a.name}
        </button>
        {a.attack && (
          <span className="text-xs text-muted-foreground">
            {signed(a.attack.bonus)} {a.attack.kind}
            {a.attack.reach ? `, ${u.label(a.attack.reach)}` : ""} · {percent(chance!)} vs AC {ac}
          </span>
        )}
        {a.save && (
          <span className="text-xs text-muted-foreground">
            {a.save.ability ? `${a.save.ability} save` : "Save"} DC {a.save.dc}
            {a.save.halfOnSuccess ? ", half on a success" : ""}
          </span>
        )}
        {a.damage.length > 0 && (
          <span className="text-xs text-muted-foreground">
            {a.damage
              .filter((d) => !d.condition)
              .map((d) => `${d.dice.map((x) => `${x.count}d${x.sides}`).join("+")}${d.bonus ? signed(d.bonus) : ""} ${d.type}`.trim())
              .join(" + ")}
          </span>
        )}
        {a.recharge && <Badge on={!c.recharging?.[a.name]}>Recharge {a.recharge === 6 ? "6" : `${a.recharge}–6`}</Badge>}
        {a.perDay && <Badge on={(c.used?.[a.name] ?? 0) < a.perDay}>{a.perDay - (c.used?.[a.name] ?? 0)}/{a.perDay} today</Badge>}
        {a.cost && a.cost > 1 && <Badge on>Costs {a.cost}</Badge>}
        {runnable && (
          <Button size="sm" variant={a.attack ? "default" : "outline"} className="ms-auto h-9 min-w-20" disabled={!usable.ok} title={usable.why} onClick={onRun}>
            {a.attack ? <DicesIcon /> : <ZapIcon />}
            {a.attack ? "Attack" : a.damage.length ? "Roll" : "Use"}
          </Button>
        )}
      </div>
      {!usable.ok && usable.why && <p className="mt-1 text-xs text-muted-foreground">Not now: {usable.why}</p>}
      {result && <p className="mt-1 rounded bg-muted/60 px-2 py-1 text-xs">{result}</p>}
      {(open || !runnable) && <p className="mt-1 text-xs leading-relaxed text-muted-foreground">{a.text}</p>}
    </div>
  );
}

function Badge({ on, children }: { on: boolean; children: ReactNode }) {
  return <span className={cn("rounded-full border px-2 py-0.5 text-[11px]", on ? "border-emerald-500/60 text-emerald-700 dark:text-emerald-300" : "border-muted-foreground/40 text-muted-foreground line-through")}>{children}</span>;
}
