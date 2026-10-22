import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { FlaskConicalIcon, Trash2Icon, UploadIcon } from "lucide-react";
import { useRef, useState } from "react";
import { HomebrewItems, HomebrewMonsters } from "./homebrew-tabs";
import { HomebrewClasses, HomebrewSpells, HomebrewSubclasses } from "./workshop-tabs";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Link, useSearchParams } from "react-router-dom";
import { toast } from "sonner";
import { ImportDiagnostics } from "@/components/import-diagnostics";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { apiClient } from "@/lib/api-client";
import { withNewBooksOff } from "@/lib/api/homebrew";
import type { DeleteHomebrewResponse, ImportDiagnostic, UploadHomebrewResponse } from "@/lib/contracts/import-diagnostics";
import { homebrewUploadError } from "@/lib/homebrew-upload-error";
import { arrangeDiagnostics } from "@/lib/import-diagnostics";
import { isAdmin } from "@/lib/player";

interface HomebrewFile {
  file: string;
  appendTargets?: string[] | null;
  elements: { name: string; type: string; source: string | null }[];
}

/**
 * The group's own content: Aurora element files (.xml). Everyone can see what is there; adding or removing files
 * needs the master admin password (key button in the header).
 */
function FilesTab() {
  const qc = useQueryClient();
  const input = useRef<HTMLInputElement>(null);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const [warnings, setWarnings] = useState<string[]>([]);
  const [diagnostics, setDiagnostics] = useState<ImportDiagnostic[]>([]);
  const admin = isAdmin();
  const { data, isLoading } = useQuery({ queryKey: ["homebrew"], queryFn: () => apiClient.get<{ files: HomebrewFile[] }>("/api/elements/homebrew") });

  const refresh = () => {
    qc.invalidateQueries({ queryKey: ["homebrew"] }).catch(() => {});
    qc.invalidateQueries({ queryKey: ["compendium"] }).catch(() => {});
    qc.invalidateQueries({ queryKey: ["sources"] }).catch(() => {});
    for (const key of ["item-catalog", "items", "builder", "sheet"]) qc.invalidateQueries({ queryKey: [key] }).catch(() => {});
  };

  const upload = useMutation({
    mutationFn: async (files: File[]) => {
      setWarnings([]);
      setDiagnostics([]);
      setUploadError(null);
      await withNewBooksOff(async () => {
        for (const file of files) {
          try {
            const result = await apiClient.post<{ fileName: string; content: string }, Partial<UploadHomebrewResponse>>("/api/elements/homebrew", { fileName: file.name, content: await file.text() });
            if (result.warnings?.length) setWarnings((current) => [...new Set([...current, ...result.warnings!])]);
            // each upload reports what its import ran into: an element already imported is not looked at again, so
            // the batch's results are kept together (the same diagnostic twice is listed once)
            if (result.diagnostics?.length) setDiagnostics((current) => arrangeDiagnostics([...current, ...result.diagnostics!]));
          } catch (error) { throw new Error(`${file.name}: ${homebrewUploadError(error as Error)}`, { cause: error }); }
        }
      });
    },
    onSuccess: (_, files) => toast.success(`${files.length} homebrew ${files.length === 1 ? "file" : "files"} added`),
    onError: (e) => { setUploadError(e.message); toast.error("Could not add the homebrew", { description: e.message }); },
    onSettled: refresh,
  });

  const remove = useMutation({
    mutationFn: (file: string) => apiClient.delete<Partial<DeleteHomebrewResponse>>(`/api/elements/homebrew/${encodeURIComponent(file)}`),
    onSuccess: (result) => {
      setWarnings(result.warnings ?? []);
      setDiagnostics(result.diagnostics ?? []);
    },
    onError: (e) => toast.error("Could not remove it", { description: e.message }),
    onSettled: refresh,
  });

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <p className="max-w-2xl text-sm text-muted-foreground">
          Your group's own species, backgrounds, feats and anything else, as Aurora element files (.xml). Spells, classes and subclasses can also be made on their tabs. They appear in the builder under the
          Homebrew books, for characters that tick Homebrew in their Sources.
        </p>
        {admin ? (
          <>
            <Button onClick={() => input.current?.click()} disabled={upload.isPending}>
              {upload.isPending ? <Spinner /> : <UploadIcon />} Add homebrew (.xml)
            </Button>
            <input
              ref={input}
              type="file"
              accept=".xml,text/xml"
              multiple
              hidden
              onChange={(e) => {
                const files = Array.from(e.target.files ?? []);
                e.target.value = "";
                if (files.length) upload.mutate(files);
              }}
            />
          </>
        ) : (
          <p className="text-sm text-muted-foreground">Unlock admin (key button at the top) to add homebrew.</p>
        )}
      </header>

      <p className="text-sm text-muted-foreground">Files can define new elements or use &lt;append id="…"&gt; to change existing ones. Wrap them in one &lt;elements&gt; root. Patches affect their original source content; they do not create a separate Homebrew book. Upload the same filename to replace a patch.</p>
      {uploadError && <p role="alert" className="rounded-md border border-destructive/40 p-3 text-sm text-destructive">{uploadError}</p>}
      {diagnostics.length > 0 && <ImportDiagnostics diagnostics={diagnostics} title="Saved, but the homebrew needs attention" />}
      {diagnostics.length === 0 && warnings.length > 0 && <div role="status" className="space-y-2 rounded-md border border-amber-500/50 bg-amber-500/5 p-3 text-sm">
        <p className="font-medium">Files saved, but some patches need attention</p>
        <ul className="list-inside list-disc break-words">{warnings.map((warning) => <li key={warning}>{warning}</li>)}</ul>
      </div>}
      {isLoading && <Spinner className="mx-auto my-8 size-6" />}
      {data?.files.length === 0 && <p className="celestial-empty text-muted-foreground">No homebrew yet.</p>}

      <div className="grid gap-4 md:grid-cols-2">
        {data?.files.map((f) => (
          <section key={f.file} className="rounded-lg border bg-background/60 p-4">
            <div className="flex items-start gap-2">
              <FlaskConicalIcon className="mt-0.5 size-4 text-muted-foreground" />
              <h2 className="min-w-0 flex-1 truncate font-medium">{f.file}</h2>
              {admin && (
                <Button
                  size="icon-sm"
                  variant="ghost"
                  aria-label={`Remove ${f.file}`}
                  disabled={remove.isPending}
                  onClick={() => {
                    if (confirm(`Remove ${f.file} and its ${f.elements.length} elements? Characters using them lose those picks.`)) remove.mutate(f.file);
                  }}
                >
                  <Trash2Icon />
                </Button>
              )}
            </div>
            <ul className="mt-2 space-y-1 text-sm">
              {f.elements
                .filter((e) => e.source !== "Internal")
                .map((e) => (
                  <li key={e.type + e.name} className="flex items-center gap-2">
                    <Link to={`/compendium?q=${encodeURIComponent(e.name)}`} className="truncate hover:underline">
                      {e.name}
                    </Link>
                    <Badge variant="outline" className="ms-auto shrink-0">
                      {e.type}
                    </Badge>
                  </li>
                ))}
            </ul>
            {!!f.appendTargets?.length && <div className="mt-3 space-y-1 text-sm">
              <p className="font-medium">Patches {f.appendTargets.length} existing {f.appendTargets.length === 1 ? "element" : "elements"}</p>
              <ul className="space-y-1 break-all text-xs text-muted-foreground">{f.appendTargets.map((id) => <li key={id}>{id}</li>)}</ul>
            </div>}
          </section>
        ))}
      </div>
    </div>
  );
}

/**
 * The group's homebrew: magic items and monsters made here, and uploaded Aurora files for anything else. Homebrew is
 * off unless switched on: a character ticks Homebrew in its Sources; a campaign switches on "Use homebrew".
 */
const TABS = ["items", "monsters", "spells", "classes", "subclasses", "files"] as const;

export function HomebrewPage() {
  // ?tab=spells opens a tab, &new=1 its "new" form (links from elsewhere can open a form directly)
  const [params, setParams] = useSearchParams();
  const tab = (TABS as readonly string[]).includes(params.get("tab") ?? "") ? params.get("tab")! : "items";
  const startNew = params.get("new") === "1";
  const open = (t: string) => setParams(t === "items" ? {} : { tab: t }, { replace: true });
  return (
    <div className="space-y-6 pb-16">
      <header className="space-y-1">
        <h1 className="font-heading text-3xl tracking-wide">Homebrew</h1>
        <p className="max-w-3xl text-muted-foreground">
          Your group's own items, monsters, classes and more. Homebrew stays hidden until it's switched on: a player ticks Homebrew in their character's
          Sources, and a DM switches on Use homebrew in the campaign's settings.
        </p>
      </header>
      <Tabs value={tab} onValueChange={open}>
        <div className="max-w-full overflow-x-auto">
          <TabsList>
            <TabsTrigger value="items">Magic items</TabsTrigger>
            <TabsTrigger value="monsters">Monsters</TabsTrigger>
            <TabsTrigger value="spells">Spells</TabsTrigger>
            <TabsTrigger value="classes">Classes</TabsTrigger>
            <TabsTrigger value="subclasses">Subclasses</TabsTrigger>
            <TabsTrigger value="files">Species & more (files)</TabsTrigger>
          </TabsList>
        </div>
        <TabsContent value="items" className="mt-4">
          <HomebrewItems startNew={startNew && tab === "items"} />
        </TabsContent>
        <TabsContent value="monsters" className="mt-4">
          <HomebrewMonsters startNew={startNew && tab === "monsters"} />
        </TabsContent>
        <TabsContent value="spells" className="mt-4">
          <HomebrewSpells startNew={startNew && tab === "spells"} />
        </TabsContent>
        <TabsContent value="classes" className="mt-4">
          <HomebrewClasses startNew={startNew && tab === "classes"} />
        </TabsContent>
        <TabsContent value="subclasses" className="mt-4">
          <HomebrewSubclasses startNew={startNew && tab === "subclasses"} />
        </TabsContent>
        <TabsContent value="files" className="mt-4">
          <FilesTab />
        </TabsContent>
      </Tabs>
    </div>
  );
}
