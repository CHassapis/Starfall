import { DownloadIcon, ShareIcon } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { promptInstall, useInstall } from "@/lib/install";

/**
 * How to install Starfall on this device: the browser's own prompt where there is one (Android and desktop Chrome or
 * Edge), Safari's steps on iPhone and iPad, and what to do on a plain http address. Nothing here opens by itself.
 */
export function InstallSteps() {
  const { canPrompt, installed, platform, secure } = useInstall();
  if (installed) return <p className="text-sm text-muted-foreground">Starfall is open as an installed app.</p>;
  return (
    <div className="space-y-3 text-sm">
      <p className="text-muted-foreground">
        Starfall can sit on your home screen and open in its own window, without the browser's address bar. It is still this website: it needs the
        server and a connection, and the address stays the same.
      </p>
      {!secure ? (
        <p className="rounded-md border border-[var(--warning)]/50 p-3">
          Installing needs the site's secure <strong>https://</strong> address. Open Starfall through that address (ask whoever runs it), then come back here.
          On this http address your browser can only make a bookmark.
        </p>
      ) : canPrompt ? (
        <Button
          onClick={async () => {
            const outcome = await promptInstall();
            if (outcome === "accepted") toast.success("Starfall is being installed");
          }}
        >
          <DownloadIcon /> Install Starfall
        </Button>
      ) : platform === "ios" ? (
        <ol className="list-decimal space-y-1.5 ps-5">
          <li>Open Starfall in <strong>Safari</strong>.</li>
          <li>
            Tap <strong>Share</strong> <ShareIcon aria-label="(the square with an arrow)" className="inline size-4 align-text-bottom" />, then{" "}
            <strong>Add to Home Screen</strong>.
          </li>
          <li>Leave <strong>Open as Web App</strong> on, then tap <strong>Add</strong>.</li>
          <li>Open Starfall from its new icon.</li>
        </ol>
      ) : platform === "android" ? (
        <ol className="list-decimal space-y-1.5 ps-5">
          <li>Open Starfall in <strong>Chrome</strong>.</li>
          <li>
            Open the menu <strong>⋮</strong> and tap <strong>Install app</strong> (or <strong>Add to home screen</strong>, then <strong>Install</strong>).
          </li>
          <li>Choose install, not "Create shortcut": a shortcut opens a normal browser tab.</li>
        </ol>
      ) : (
        <p>
          In Chrome or Edge, use the install button at the end of the address bar, or the browser menu's <strong>Install Starfall</strong> (Edge:{" "}
          <strong>Apps › Install this site as an app</strong>). Firefox and Safari on a computer don't install sites.
        </p>
      )}
      {platform === "ios" && secure && (
        <p className="text-xs text-muted-foreground">
          On iPhone and iPad the installed app keeps its own storage: choose your player (and unlock a locked character) once more inside it; the theme and
          units too.
        </p>
      )}
    </div>
  );
}

/** the Install Starfall dialog, opened from a menu */
export function InstallStarfallDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Install Starfall</DialogTitle>
          <DialogDescription>Put Starfall on your home screen or desktop.</DialogDescription>
        </DialogHeader>
        <InstallSteps />
      </DialogContent>
    </Dialog>
  );
}
