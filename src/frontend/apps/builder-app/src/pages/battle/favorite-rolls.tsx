/**
 * Favorite rolls: dice the player names and keeps on the character, rolled with one press. The sheet and the battle
 * view show the same list, kept in the combat state (lib/rules/favorite-rolls); a roll is shown as the compendium's
 * dice show one, each die and the total.
 */
import { DicesIcon, PencilIcon, PlusIcon, Trash2Icon } from "lucide-react";
import { useId, useState } from "react";
import { showRoll } from "@/components/lore/dice-toast";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { describeRoll, roll } from "@/lib/lore/dice";
import { diceRange, favoriteDice, MAX_FAVORITE_ROLLS, MAX_ROLL_FORMULA, MAX_ROLL_NAME, newFavoriteRollId, type FavoriteRoll } from "@/lib/rules/favorite-rolls";
import { cn } from "@/lib/utils";

/** The list with its last result, and the dialog that adds, changes or removes a roll. */
export function FavoriteRolls({ rolls, onSave, onRemove, className }: { rolls: FavoriteRoll[]; onSave: (r: FavoriteRoll) => void; onRemove: (id: string) => void; className?: string }) {
  const [editing, setEditing] = useState<FavoriteRoll | "new" | null>(null);
  const [last, setLast] = useState<string | null>(null);
  const rollIt = (f: FavoriteRoll) => {
    const parsed = favoriteDice(f.formula);
    if (!parsed) return;
    const r = roll(parsed);
    showRoll(f.name, f.formula, r);
    setLast(`${f.name}: ${r.total} (${f.formula.replace(/\s+/g, " ")} → ${describeRoll(r)})`);
  };
  return (
    <div className={cn("space-y-2", className)}>
      {rolls.length ? (
        <ul className="flex flex-wrap gap-2" aria-label="Favorite rolls">
          {rolls.map((f) => (
            <li key={f.id} className="inline-flex min-w-0 max-w-full overflow-hidden rounded-md border border-border bg-muted">
              <button
                type="button"
                className="flex min-h-9 min-w-0 items-center gap-1.5 px-2.5 text-left text-sm text-foreground hover:bg-accent focus-visible:outline-2 focus-visible:outline-ring disabled:opacity-50"
                aria-label={`Roll ${f.name}: ${f.formula}`}
                disabled={!favoriteDice(f.formula)}
                onClick={() => rollIt(f)}
              >
                <DicesIcon className="size-4 shrink-0" aria-hidden />
                <span className="truncate font-medium">{f.name}</span>
                <span className="shrink-0 text-muted-foreground">{f.formula}</span>
              </button>
              <button
                type="button"
                className="flex min-h-9 w-9 shrink-0 items-center justify-center border-l border-border text-muted-foreground hover:bg-accent focus-visible:outline-2 focus-visible:outline-ring"
                aria-label={`Change ${f.name}`}
                onClick={() => setEditing(f)}
              >
                <PencilIcon className="size-3.5" />
              </button>
            </li>
          ))}
        </ul>
      ) : (
        <p className="text-sm text-muted-foreground">Name the dice you roll often, for example "Sneak Attack: 3d6", and roll them here with one press.</p>
      )}
      <p role="status" className={cn("text-sm", !last && "sr-only")}>
        {last && `Last roll: ${last}`}
      </p>
      <Button size="sm" variant="outline" className="h-9 sm:h-8" disabled={rolls.length >= MAX_FAVORITE_ROLLS} onClick={() => setEditing("new")}>
        <PlusIcon /> Add a favorite roll
      </Button>
      {editing && (
        <FavoriteRollDialog
          favorite={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
          onSave={(r) => {
            onSave(r);
            setEditing(null);
          }}
          onRemove={(id) => {
            onRemove(id);
            setEditing(null);
          }}
        />
      )}
    </div>
  );
}

function FavoriteRollDialog({ favorite, onClose, onSave, onRemove }: { favorite: FavoriteRoll | null; onClose: () => void; onSave: (r: FavoriteRoll) => void; onRemove: (id: string) => void }) {
  const [name, setName] = useState(favorite?.name ?? "");
  const [formula, setFormula] = useState(favorite?.formula ?? "");
  const help = useId();
  const parsed = favoriteDice(formula);
  const range = parsed && diceRange(parsed);
  const ready = !!name.trim() && !!parsed;
  const save = () => ready && onSave({ id: favorite?.id ?? newFavoriteRollId(), name: name.trim(), formula: formula.trim() });
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{favorite ? `Change ${favorite.name}` : "Add a favorite roll"}</DialogTitle>
          <DialogDescription>Kept on this character: the sheet and the battle view show the same rolls.</DialogDescription>
        </DialogHeader>
        <form
          id={`${help}-form`}
          className="space-y-3"
          onSubmit={(e) => {
            e.preventDefault();
            save();
          }}
        >
          <label className="block space-y-1 text-sm">
            <span className="font-medium">Name</span>
            <Input value={name} onChange={(e) => setName(e.target.value)} maxLength={MAX_ROLL_NAME} placeholder="e.g. Divine Smite" autoFocus />
          </label>
          <label className="block space-y-1 text-sm">
            <span className="font-medium">Dice</span>
            <Input value={formula} onChange={(e) => setFormula(e.target.value)} maxLength={MAX_ROLL_FORMULA} placeholder="e.g. 2d8+3" aria-invalid={!!formula.trim() && !parsed} aria-describedby={help} inputMode="text" autoCapitalize="off" autoCorrect="off" spellCheck={false} />
          </label>
          <p id={help} className={cn("text-sm", formula.trim() && !parsed ? "text-destructive" : "text-muted-foreground")} aria-live="polite">
            {!formula.trim()
              ? "Dice and a bonus, like 2d6+3, d20+5 or 4d6 + 1d4 - 1."
              : range
                ? `Rolls ${range.min} to ${range.max}.`
                : "Not dice the app can roll: write them like 2d6+3, with at most 100 dice of up to 1000 sides each."}
          </p>
        </form>
        <DialogFooter className="gap-2 sm:justify-between">
          {favorite ? (
            <Button variant="ghost" onClick={() => onRemove(favorite.id)}>
              <Trash2Icon /> Remove
            </Button>
          ) : (
            <span />
          )}
          <div className="flex gap-2">
            <Button variant="outline" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" form={`${help}-form`} disabled={!ready}>
              {favorite ? "Save" : "Add"}
            </Button>
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
