import { PaletteIcon } from "lucide-react";
import { useState } from "react";
import { useTheme } from "@/components/theme-provider";
import { THEMES, type Theme } from "@/lib/themes";
import { useUnits } from "@/components/units-provider";
import { UNITS } from "@/lib/units";
import { InstallSteps } from "@/components/install-starfall";
import { useInstall } from "@/lib/install";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle, DialogTrigger } from "@/components/ui/dialog";

export function ModeToggle() {
  const { theme, setTheme, previewTheme } = useTheme();
  const { units, setUnits } = useUnits();
  const { installed } = useInstall();
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState<Theme>(theme);
  function close(next: boolean) {
    setOpen(next);
    previewTheme(null);
    if (next) setDraft(theme);
  }
  return (
    <Dialog open={open} onOpenChange={close}>
      <DialogTrigger asChild>
        <Button variant="ghost" size="icon" aria-label="Choose theme and units" title="Theme and measurement units">
          <PaletteIcon />
        </Button>
      </DialogTrigger>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Make yourself at home</DialogTitle>
          <DialogDescription>Preview a theme, then save it for this browser. Measurement units change at once.</DialogDescription>
        </DialogHeader>
        <div className="grid gap-3" role="group" aria-label="Theme preview">
          {THEMES.map((t) => (
            <button
              type="button"
              key={t.id}
              aria-pressed={draft === t.id}
              onClick={() => {
                setDraft(t.id);
                previewTheme(t.id);
              }}
              className="flex min-h-20 items-center gap-4 rounded-xl border p-4 text-left hover:bg-muted aria-pressed:ring-2 aria-pressed:ring-ring"
            >
              <span className="flex shrink-0 -space-x-2" aria-hidden>
                {t.colors.map((color) => (
                  <span key={color} className="size-7 rounded-full border border-black/20" style={{ background: color }} />
                ))}
              </span>
              <span>
                <span className="block font-semibold">{t.name}</span>
                <span className="text-sm text-muted-foreground">{t.description}</span>
              </span>
            </button>
          ))}
          <Button
            variant="outline"
            aria-pressed={draft === "system"}
            onClick={() => {
              setDraft("system");
              previewTheme("system");
            }}
          >
            Follow device appearance
          </Button>
        </div>
        {/* display only: the rules, saved characters, the PDF sheet and Aurora files keep feet and pounds */}
        <div className="grid gap-2 border-t pt-4">
          <h3 id="units-heading" className="text-sm font-semibold">Measurement units</h3>
          <div role="group" aria-labelledby="units-heading" className="grid grid-cols-2 gap-2">
            {UNITS.map((u) => (
              <Button key={u} variant="outline" aria-pressed={units === u} onClick={() => setUnits(u)} className="h-auto flex-col gap-0 py-2 aria-pressed:ring-2 aria-pressed:ring-ring">
                <span className="font-semibold">{u === "imperial" ? "Imperial" : "Metric"}</span>
                <span className="text-xs font-normal text-muted-foreground">{u === "imperial" ? "feet, pounds" : "metres, kilograms"}</span>
              </Button>
            ))}
          </div>
          <p className="text-xs text-muted-foreground">
            Speeds, ranges, weights and carrying capacity follow your choice (5 ft = 1.5 m, 1 lb = 0.5 kg, as at most tables). Rules text, the printed sheet and Aurora files keep feet and pounds.
          </p>
        </div>
        {!installed && (
          <details className="border-t pt-4">
            <summary className="text-sm font-semibold">Install Starfall as an app</summary>
            <div className="mt-3">
              <InstallSteps />
            </div>
          </details>
        )}
        <div className="flex flex-wrap justify-end gap-2">
          <Button variant="ghost" onClick={() => close(false)}>
            Cancel
          </Button>
          <Button
            onClick={() => {
              setTheme(draft);
              setOpen(false);
            }}
          >
            Save theme
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}
