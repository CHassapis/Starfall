import { useEffect, useRef, useState } from "react";
import { toast } from "sonner";
import { ImportDiagnostics } from "@/components/import-diagnostics";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { DialogFooter } from "@/components/ui/dialog";
import { Spinner } from "@/components/ui/spinner";
import { previewHomebrew, StaleHomebrewError, type HomebrewKind } from "@/lib/api/homebrew";
import { canSave, fieldLabel, previewSummary, revisionToSend, type HomebrewEditPreview } from "@/lib/homebrew-preview";
import { cn } from "@/lib/utils";

const CHANGE_LABEL = { added: "Added", changed: "Changed", removed: "Removed" } as const;

/**
 * What saving would do: what changes (fields, features), which characters use it and what they would lose, and what
 * is wrong with it. Shown before a Homebrew page save.
 */
export function EditPreview({ preview }: { preview: HomebrewEditPreview }) {
  const names = new Map(preview.elements.map((e) => [e.elementId, e.name]));
  const nameOf = (id: string) => names.get(id) ?? (id === preview.id ? "it" : id);
  return (
    <section aria-label="What saving does" className="min-w-0 space-y-3 rounded-md border bg-muted/30 p-3 text-sm">
      <p className="font-medium">{previewSummary(preview)}</p>
      <ImportDiagnostics diagnostics={preview.diagnostics} />
      {preview.changes.length > 0 && (
        <ul className="space-y-1">
          {preview.changes.map((c) => (
            <li key={c.field} className="[overflow-wrap:anywhere]">
              <span className="text-muted-foreground">{fieldLabel(c.field)}:</span> {c.before || "—"} <span aria-label="becomes">→</span> {c.after || "—"}
            </li>
          ))}
        </ul>
      )}
      {preview.elements.some((e) => e.elementId !== preview.id) && (
        <ul className="space-y-1">
          {preview.elements
            .filter((e) => e.elementId !== preview.id)
            .map((e) => (
              <li key={e.elementId} className="flex flex-wrap items-center gap-2">
                <Badge variant={e.change === "removed" ? "destructive" : "outline"}>{CHANGE_LABEL[e.change]}</Badge>
                <span className="min-w-0 [overflow-wrap:anywhere]">{e.name}</span>
              </li>
            ))}
        </ul>
      )}
      {preview.characters.length > 0 && (
        <div>
          <p className="text-muted-foreground">Characters that use it</p>
          <ul className="mt-1 space-y-1">
            {preview.characters.map((c) => (
              <li key={c.characterId} className="[overflow-wrap:anywhere]">
                <span className="font-medium">{c.name}</span>
                {c.playerName && <span className="text-muted-foreground"> ({c.playerName})</span>}
                <span className="text-muted-foreground">: {c.uses.map(nameOf).join(", ")}</span>
                {c.loses.length > 0 && <span className={cn("block text-destructive")}>Loses {c.loses.map(nameOf).join(", ")} at their next update.</span>}
              </li>
            ))}
          </ul>
        </div>
      )}
    </section>
  );
}

/**
 * A Homebrew page form's footer: "Review and save" asks the server what the save would do and shows it; "Save"
 * then saves with the revision the form was opened on, so a record someone changed in the meantime is not
 * overwritten unseen (the review comes back, fresh, saying so; saving again then goes through). Changing the form
 * after the review asks for a new one.
 */
export function ReviewFooter<T extends { revision?: string | null }>({
  kind,
  model,
  ready,
  busy,
  onClose,
  onSave,
}: {
  kind: HomebrewKind;
  model: T;
  ready: boolean;
  busy: boolean;
  onClose: () => void;
  onSave: (model: T) => Promise<unknown>;
}) {
  // the review holds for the form as it was reviewed (each edit makes a new model)
  const [reviewed, setReviewed] = useState<{ preview: HomebrewEditPreview; model: T } | null>(null);
  const preview = reviewed?.model === model ? reviewed.preview : null;
  const shown = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (preview) shown.current?.scrollIntoView({ block: "nearest" });
  }, [preview]);
  // set once a save was refused (409): its fresh review was shown, so the next save is based on it
  const [refused, setRefused] = useState<HomebrewEditPreview | null>(null);
  const [checking, setChecking] = useState(false);
  const review = async () => {
    setChecking(true);
    try {
      setReviewed({ preview: await previewHomebrew(kind, model), model });
    } catch (e) {
      toast.error("Could not check it", { description: (e as Error).message });
    } finally {
      setChecking(false);
    }
  };
  const save = async () => {
    try {
      await onSave({ ...model, expectedRevision: revisionToSend(model.revision, refused) });
    } catch (e) {
      if (e instanceof StaleHomebrewError) {
        setRefused(e.preview);
        setReviewed({ preview: e.preview, model });
      } else toast.error("Could not save it", { description: (e as Error).message });
    }
  };
  if (preview) {
    return (
      <div ref={shown} className="min-w-0 space-y-4">
        <EditPreview preview={preview} />
        <DialogFooter>
          <Button variant="outline" onClick={() => setReviewed(null)}>
            Back to editing
          </Button>
          <Button disabled={busy || !canSave(preview)} onClick={save}>
            {busy && <Spinner />} Save
          </Button>
        </DialogFooter>
      </div>
    );
  }
  return (
    <DialogFooter>
      <Button variant="outline" onClick={onClose}>
        Cancel
      </Button>
      <Button disabled={checking || !ready} onClick={review}>
        {checking && <Spinner />} Review and save
      </Button>
    </DialogFooter>
  );
}
