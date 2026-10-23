/**
 * Resources the player adds by hand: a homebrew feature's uses, a pool of points other features spend from, a switch
 * with an effect. They are kept in the character's combat state with what was spent (CombatState.resources), and the
 * battle model lists them with the sheet's features, so the sheet, the battle view and the rests treat them alike.
 * A tracker with a stable id, a maximum that follows the character and its own recharge is the shape of Forge
 * Character Builder's trackers (MIT); no code is taken from it.
 */
import { PencilIcon, PlusIcon, Trash2Icon } from "lucide-react";
import { useState, type ReactNode } from "react";
import { WhyValue } from "@/components/why-value";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import type { SheetData } from "@/lib/api/sheet";
import { explainResource, resourceFacts, type BattleFeature } from "@/lib/battle-model";
import { newResourceId, RESOURCE_DAMAGE, resourceMax, resourceTraceKey, type CustomResource, type ResourceMax } from "@/lib/rules/battle";
import { cn } from "@/lib/utils";

const field = "h-9 w-full rounded-md border border-border bg-muted px-2 text-sm text-foreground";
const MAX_FROM: { value: ResourceMax["from"]; label: string }[] = [
  { value: "number", label: "A number" },
  { value: "proficiency", label: "Proficiency bonus" },
  { value: "ability", label: "An ability modifier" },
  { value: "level", label: "Character level" },
  { value: "class-level", label: "A class's level" },
];
const RECHARGES: { value: CustomResource["recharge"]; label: string }[] = [
  { value: "long", label: "A long rest" },
  { value: "short", label: "A short or long rest" },
  { value: "dawn", label: "Dawn (a new day)" },
  { value: "turn", label: "Each turn" },
  { value: "never", label: "Only when you undo it" },
];
const ABILITIES = ["STR", "DEX", "CON", "INT", "WIS", "CHA"];

const blank = (): CustomResource => ({ id: newResourceId(), name: "", max: { from: "number", value: 1 }, recharge: "long", pool: null, cost: 1, effect: null, notes: null });
const num = (text: string, fallback = 0) => (text.trim() === "" || Number.isNaN(Number(text)) ? fallback : Math.trunc(Number(text)));

function Labelled({ label, children, wide }: { label: string; children: ReactNode; wide?: boolean }) {
  return (
    <label className={cn("block space-y-1 text-sm", wide && "sm:col-span-2")}>
      <span className="font-medium">{label}</span>
      {children}
    </label>
  );
}

/** Adding, changing or removing a resource; removing it also forgets what was spent of it and switches its effect off. */
export function ResourceDialog({
  data,
  features,
  resource,
  onClose,
  onSave,
  onRemove,
}: {
  data: SheetData;
  /** the features with uses of their own, which a resource can spend from */
  features: BattleFeature[];
  /** the one changed; null for a new one */
  resource: CustomResource | null;
  onClose: () => void;
  onSave: (r: CustomResource) => void;
  onRemove: (id: string) => void;
}) {
  const [r, setR] = useState<CustomResource>(() => resource ?? blank());
  const [pooled, setPooled] = useState(!!resource?.pool);
  const [removing, setRemoving] = useState(false);
  const set = (patch: Partial<CustomResource>) => setR((x) => ({ ...x, ...patch }));
  const setMax = (patch: Partial<ResourceMax>) => setR((x) => ({ ...x, max: { ...x.max, ...patch } }));
  const setEffect = (patch: Partial<NonNullable<CustomResource["effect"]>>) => setR((x) => ({ ...x, effect: { ...(x.effect ?? {}), ...patch } }));
  const pools = features.filter((f) => f.parsedUsage?.kind === "count" && f.resource?.id !== r.id).map((f) => f.title);
  const classes = data.classLevels ?? [];
  const max = resourceMax(r, resourceFacts(data));
  const damageOk = !r.effect?.damage?.trim() || RESOURCE_DAMAGE.test(r.effect.damage.trim());
  const ready = !!r.name.trim() && (!pooled || !!r.pool?.trim()) && (r.max.from !== "class-level" || !!r.max.className?.trim()) && damageOk;
  const save = () => {
    const effect = r.effect && (r.effect.armorClass || r.effect.speed || r.effect.attackRolls || r.effect.damage?.trim()) ? { ...r.effect, damage: r.effect.damage?.trim() || null } : null;
    onSave({
      ...r,
      name: r.name.trim(),
      pool: pooled ? r.pool!.trim() : null,
      cost: pooled ? Math.max(1, r.cost ?? 1) : 1,
      max: { ...r.max, ability: r.max.from === "ability" ? (r.max.ability ?? "CHA") : null, className: r.max.from === "class-level" ? r.max.className!.trim() : null },
      effect,
      notes: r.notes?.trim() || null,
    });
  };
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-xl">
        <DialogHeader>
          <DialogTitle>{resource ? `Change ${resource.name}` : "Add a resource"}</DialogTitle>
          <DialogDescription>Uses you count yourself: a homebrew feature, a pool of points, or a switch with an effect. The sheet and the battle view show the same one.</DialogDescription>
        </DialogHeader>
        <div className="grid gap-3 sm:grid-cols-2">
          <Labelled label="Name" wide>
            <Input value={r.name} onChange={(e) => set({ name: e.target.value })} maxLength={60} autoFocus />
          </Labelled>
          <fieldset className="space-y-1.5 sm:col-span-2">
            <legend className="text-sm font-medium">Its uses</legend>
            <div className="flex flex-wrap gap-1.5">
              {[
                { on: false, label: "Its own" },
                { on: true, label: "Spent from another resource" },
              ].map((o) => (
                <label key={o.label} className={cn("inline-flex min-h-9 cursor-pointer items-center gap-1.5 rounded-md border px-2.5 text-sm", pooled === o.on ? "border-primary/60 bg-primary/10" : "text-muted-foreground")}>
                  <input type="radio" name="uses" className="size-4" checked={pooled === o.on} onChange={() => setPooled(o.on)} />
                  {o.label}
                </label>
              ))}
            </div>
          </fieldset>
          {pooled ? (
            <>
              <Labelled label="Spends from">
                <Input value={r.pool ?? ""} onChange={(e) => set({ pool: e.target.value })} maxLength={100} list="resource-pools" placeholder="e.g. Channel Divinity" />
                <datalist id="resource-pools">
                  {pools.map((p) => (
                    <option key={p} value={p} />
                  ))}
                </datalist>
              </Labelled>
              <Labelled label="Each use costs">
                <Input type="number" min={1} max={99} value={r.cost ?? 1} onChange={(e) => set({ cost: Math.min(99, Math.max(1, num(e.target.value, 1))) })} />
              </Labelled>
            </>
          ) : (
            <>
              <Labelled label="Maximum follows">
                <select className={field} value={r.max.from} onChange={(e) => setMax({ from: e.target.value as ResourceMax["from"] })}>
                  {MAX_FROM.map((m) => (
                    <option key={m.value} value={m.value}>
                      {m.label}
                    </option>
                  ))}
                </select>
              </Labelled>
              {r.max.from === "number" && (
                <Labelled label="Uses">
                  <Input type="number" min={0} max={999} value={r.max.value ?? 0} onChange={(e) => setMax({ value: Math.min(999, Math.max(0, num(e.target.value))) })} />
                </Labelled>
              )}
              {r.max.from === "ability" && (
                <Labelled label="Ability">
                  <select className={field} value={r.max.ability ?? "CHA"} onChange={(e) => setMax({ ability: e.target.value })}>
                    {ABILITIES.map((a) => (
                      <option key={a}>{a}</option>
                    ))}
                  </select>
                </Labelled>
              )}
              {r.max.from === "class-level" && (
                <Labelled label="Class">
                  <Input value={r.max.className ?? ""} onChange={(e) => setMax({ className: e.target.value })} maxLength={60} list="resource-classes" placeholder={classes[0]?.name ?? "e.g. Paladin"} />
                  <datalist id="resource-classes">
                    {classes.map((c) => (
                      <option key={c.name} value={c.name} />
                    ))}
                  </datalist>
                </Labelled>
              )}
              {r.max.from !== "number" && (
                <>
                  <Labelled label="Times">
                    <Input type="number" min={1} max={100} value={r.max.times ?? 1} onChange={(e) => setMax({ times: Math.min(100, Math.max(1, num(e.target.value, 1))) })} />
                  </Labelled>
                  <Labelled label="Plus">
                    <Input type="number" min={-99} max={999} value={r.max.plus ?? 0} onChange={(e) => setMax({ plus: Math.min(999, Math.max(-99, num(e.target.value))) })} />
                  </Labelled>
                  <Labelled label="At least">
                    <Input type="number" min={0} max={999} value={r.max.atLeast ?? ""} onChange={(e) => setMax({ atLeast: e.target.value.trim() === "" ? null : Math.min(999, Math.max(0, num(e.target.value))) })} />
                  </Labelled>
                </>
              )}
              <Labelled label="Comes back on">
                <select className={field} value={r.recharge} onChange={(e) => set({ recharge: e.target.value as CustomResource["recharge"] })}>
                  {RECHARGES.map((x) => (
                    <option key={x.value} value={x.value}>
                      {x.label}
                    </option>
                  ))}
                </select>
              </Labelled>
              <p className="text-sm text-muted-foreground sm:col-span-2" aria-live="polite">
                Now: {max} use{max === 1 ? "" : "s"}.
              </p>
            </>
          )}
          <fieldset className="grid gap-3 rounded-md border p-3 sm:col-span-2 sm:grid-cols-2">
            <legend className="px-1 text-sm font-medium">While switched on (optional)</legend>
            <Labelled label="Armor class">
              <Input type="number" min={-20} max={20} value={r.effect?.armorClass ?? ""} onChange={(e) => setEffect({ armorClass: Math.min(20, Math.max(-20, num(e.target.value))) || undefined })} />
            </Labelled>
            <Labelled label="Speed (ft)">
              <Input type="number" min={-120} max={120} step={5} value={r.effect?.speed ?? ""} onChange={(e) => setEffect({ speed: Math.min(120, Math.max(-120, num(e.target.value))) || undefined })} />
            </Labelled>
            <Labelled label="Attack rolls">
              <Input type="number" min={-20} max={20} value={r.effect?.attackRolls ?? ""} onChange={(e) => setEffect({ attackRolls: Math.min(20, Math.max(-20, num(e.target.value))) || undefined })} />
            </Labelled>
            <Labelled label="Weapon damage">
              <Input value={r.effect?.damage ?? ""} onChange={(e) => setEffect({ damage: e.target.value })} maxLength={30} placeholder="e.g. 1d6 fire" aria-invalid={!damageOk} />
            </Labelled>
          </fieldset>
          <Labelled label="Notes" wide>
            <textarea className={cn(field, "h-auto py-2")} rows={2} value={r.notes ?? ""} onChange={(e) => set({ notes: e.target.value })} maxLength={500} />
          </Labelled>
        </div>
        <DialogFooter className="gap-2 sm:justify-between">
          {resource ? (
            removing ? (
              <Button variant="destructive" onClick={() => onRemove(resource.id)}>
                <Trash2Icon /> Remove it and what was spent
              </Button>
            ) : (
              <Button variant="ghost" onClick={() => setRemoving(true)}>
                <Trash2Icon /> Remove
              </Button>
            )
          ) : (
            <span />
          )}
          <div className="flex gap-2">
            <Button variant="outline" onClick={onClose}>
              Cancel
            </Button>
            <Button disabled={!ready} onClick={save}>
              {resource ? "Save" : "Add"}
            </Button>
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/** "Add a resource", with its dialog. */
export function AddResource(props: { data: SheetData; features: BattleFeature[]; onSave: (r: CustomResource) => void }) {
  const [open, setOpen] = useState(false);
  return (
    <>
      <Button size="sm" variant="secondary" className="h-9 sm:h-7" onClick={() => setOpen(true)}>
        <PlusIcon /> Add a resource
      </Button>
      {open && (
        <ResourceDialog
          {...props}
          resource={null}
          onClose={() => setOpen(false)}
          onSave={(r) => {
            props.onSave(r);
            setOpen(false);
          }}
          onRemove={() => setOpen(false)}
        />
      )}
    </>
  );
}

/** A resource's own controls in a list: "Why?" for its maximum, its effect switch, and changing it. */
export function ResourceControls({
  characterId,
  data,
  features,
  feature,
  effectOn,
  onSwitch,
  onSave,
  onRemove,
}: {
  characterId: string;
  data: SheetData;
  features: BattleFeature[];
  feature: BattleFeature;
  effectOn: boolean;
  onSwitch: (on: boolean) => void;
  onSave: (r: CustomResource) => void;
  onRemove: (id: string) => void;
}) {
  const [editing, setEditing] = useState(false);
  const r = feature.resource!;
  const counted = feature.parsedUsage?.kind === "count" ? feature.parsedUsage : null;
  return (
    <>
      {counted && <WhyValue characterId={characterId} traceKey={resourceTraceKey(r.id)} label={`${r.name} maximum uses`} explain={explainResource(data, r)} shown={counted.max} />}
      {r.effect && (
        <Button size="sm" variant={effectOn ? "default" : "secondary"} className="h-9 sm:h-7" aria-pressed={effectOn} aria-label={`${r.name}: ${effectOn ? "on" : "switch on"}`} onClick={() => onSwitch(!effectOn)}>
          {effectOn ? "On" : "Switch on"}
        </Button>
      )}
      <Button size="sm" variant="ghost" className="h-9 text-muted-foreground sm:h-7" aria-label={`Change ${r.name}`} onClick={() => setEditing(true)}>
        <PencilIcon />
      </Button>
      {editing && (
        <ResourceDialog
          data={data}
          features={features}
          resource={r}
          onClose={() => setEditing(false)}
          onSave={(next) => {
            onSave(next);
            setEditing(false);
          }}
          onRemove={(id) => {
            onRemove(id);
            setEditing(false);
          }}
        />
      )}
    </>
  );
}

