import { ArrowDownIcon, ArrowLeftIcon, ChurchIcon, CircleAlertIcon, CircleCheckIcon, FileTextIcon, ImageIcon, InfoIcon, MinusIcon, PlusIcon, ScaleIcon, ScrollTextIcon, SparklesIcon, SwordsIcon, Trash2Icon, UserIcon, UsersIcon } from "lucide-react";
import { useMemo, useRef, useState, type ComponentType, type ReactNode } from "react";
import { Link, useParams, useSearchParams } from "react-router-dom";
import { SaveBar } from "./save-bar";
import { toast } from "sonner";
import { ElementPanel } from "@/components/element-panel";
import { SearchSelect } from "@/components/search-select";
import { SPELL_FACETS, spellDetails, spellFacets, spellHint, spellKind } from "@/lib/rules/option-filters";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Drawer, DrawerContent, DrawerHeader, DrawerTitle } from "@/components/ui/drawer";
import { Spinner } from "@/components/ui/spinner";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { useIsMobile } from "@/hooks/use-mobile";
import {
  useAssignPlayer,
  useRemovePortrait,
  useBuilderChoices,
  useCharacterClassList,
  useCharacterHeader,
  useChoiceOptions,
  useClearChoice,
  usePickChoice,
  useSetClassLevel,
  useMulticlass,
  useAddMulticlass,
  useRemoveMulticlass,
  type BuilderChoice,
} from "@/lib/api/builder";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { usePlayer } from "@/lib/player";
import { firstImage, usePictureDrop, usePortraitUpload } from "@/lib/picture-drop";
import { SourcesPicker } from "@/components/sources-picker";
import { StoryTab } from "./story-tab";
import { AbilitiesTab } from "./abilities-tab";
import { EquipmentTab } from "./equipment-tab";
import { SpellChoice } from "./spell-choice";
import { MagicTab } from "./magic-tab";
import { AuroraUpdateDialog } from "../aurora-import-dialog";
import { CharacterCampaigns } from "./character-campaigns";
import { ExtrasCard } from "./extras-card";
import { Wizard } from "./wizard";
import { editionOf, switchEdition, useCharacterSources, useSetCharacterSources, useSources } from "@/lib/api/sources";
import { EditionPicker } from "@/components/edition-picker";
import { isOpen, openByTab, stepChoices, type BuilderTab } from "@/lib/rules/wizard-steps";
import { cn } from "@/lib/utils";

import { useUnits } from "@/components/units-provider";
const SECTION_ORDER = ["Class", "Species", "Background", "Alignment", "Deity"];
const SECTION_ICONS: Record<string, ComponentType<{ className?: string }>> = {
  Class: SwordsIcon,
  Species: UsersIcon,
  Background: ScrollTextIcon,
  Alignment: ScaleIcon,
  Deity: ChurchIcon,
};

export function CharacterBuilderPage() {
  const { id = "" } = useParams();
  const isMobile = useIsMobile();
  const { data: choicesData, isLoading } = useBuilderChoices(id);
  const header = useCharacterHeader(id);

  const [preview, setPreview] = useState<string | null>(null); // option under the pointer in an open list
  const [focus, setFocus] = useState<string | null>(null); // last picked or inspected element
  const [drawerId, setDrawerId] = useState<string | null>(null);
  const [tab, setTab] = useState("build");
  const [params, setParams] = useSearchParams();
  const guided = params.get("guide") === "1";

  // spells are chosen on the Magic tab (as in Aurora), everything else on the Build tab
  const spellChoices = useMemo(() => stepChoices({ spells: true }, choicesData?.choices ?? []), [choicesData]);
  const openSpells = spellChoices.filter((c) => !c.optional && !c.selected).length;
  const openTabs = useMemo(() => openByTab(choicesData?.choices ?? []), [choicesData]);
  const sections = useMemo(() => {
    const bySection = new Map<string, BuilderChoice[]>();
    for (const choice of (choicesData?.choices ?? []).filter((c) => c.type !== "Spell")) {
      if (!bySection.has(choice.section)) bySection.set(choice.section, []);
      bySection.get(choice.section)!.push(choice);
    }
    const rank = (s: string) => (SECTION_ORDER.includes(s) ? SECTION_ORDER.indexOf(s) : SECTION_ORDER.length);
    return [...bySection.entries()].sort(([a], [b]) => rank(a) - rank(b));
  }, [choicesData]);

  function inspect(elementId: string) {
    setFocus(elementId);
    if (isMobile) setDrawerId(elementId);
  }

  if (header.isError) {
    const locked = (header.error as { status?: number }).status === 401;
    return (
      <div className="container mx-auto px-4 py-16 text-center">
        <p className="text-muted-foreground">
          {locked
            ? "This character belongs to a player who locked their characters with a password. Pick that player (and enter the password) to open it."
            : "This character does not exist (any more)."}
        </p>
        <Link to="/characters" className="mt-4 inline-block underline">
          Back to the characters
        </Link>
      </div>
    );
  }

  return (
    <div className="container mx-auto space-y-6 px-4 py-8 pb-24 4xl:max-w-[125rem]">
      <BuilderHeader characterId={id} choices={choicesData?.choices ?? []} pending={choicesData?.pending ?? false} guided={guided} />
      <SaveBar characterId={id} name={header.data?.character.name ?? ""} />

      {guided ? (
        <Wizard
          characterId={id}
          choices={choicesData?.choices ?? []}
          renderChoice={(choice, hideLabel) => (
            <ChoiceRow key={choice.ruleId} characterId={id} choice={choice} hideLabel={hideLabel} onHighlight={setPreview} onPicked={setFocus} onInspect={inspect} />
          )}
          rules={<SourcesTab characterId={id} />}
          aside={
            <div className="sticky top-20 max-h-[calc(100vh-6rem)] overflow-y-auto rounded-lg border bg-background/60 p-5">
              <ElementPanel id={preview ?? focus} emptyHint="Open a list and point at an option to read about it here." />
            </div>
          }
          onExit={() => setParams({}, { replace: true })}
        />
      ) : (
      <Tabs value={tab} onValueChange={setTab}>
        <ToDoBar open={openTabs} loading={isLoading} tab={tab} setTab={setTab} />
        <div className="flex flex-wrap items-center gap-2">
        <TabsList className="h-auto max-w-full flex-wrap justify-start">
          <TabsTrigger value="build">
            Build
            {openTabs.build > 0 && <span className="ms-1 rounded-full bg-amber-500/90 px-1.5 text-[11px] text-black">{openTabs.build}</span>}
          </TabsTrigger>
          <TabsTrigger value="abilities">Abilities</TabsTrigger>
          <TabsTrigger value="magic">
            Magic
            {openSpells > 0 && <span className="ms-1 rounded-full bg-amber-500/90 px-1.5 text-[11px] text-black">{openSpells}</span>}
          </TabsTrigger>
          <TabsTrigger value="equipment">
            Equipment
            {openTabs.equipment > 0 && <span className="ms-1 rounded-full bg-amber-500/90 px-1.5 text-[11px] text-black">{openTabs.equipment}</span>}
          </TabsTrigger>
          <TabsTrigger value="story">Story</TabsTrigger>
          <TabsTrigger value="sources">Sources</TabsTrigger>
        </TabsList>
          <Button size="sm" variant="ghost" className="text-muted-foreground" onClick={() => setParams({ guide: "1" })}>
            Step by step
          </Button>
        </div>

        <TabsContent value="abilities" className="mt-4">
          <AbilitiesTab characterId={id} />
        </TabsContent>

        <TabsContent value="magic" className="mt-4">
          <MagicTab characterId={id} spellChoices={spellChoices} renderChoice={(choice) => <SpellChoice key={`${choice.registrationId}:${choice.ruleId}`} characterId={id} choice={choice} />} />
        </TabsContent>

        <TabsContent value="equipment" className="mt-4">
          <EquipmentTab
            characterId={id}
            itemChoices={sections.find(([section]) => section === "Equipment")?.[1] ?? []}
            renderChoice={(choice) => (
              <ChoiceRow key={choice.ruleId} characterId={id} choice={choice} hideLabel={false} onHighlight={setPreview} onPicked={setFocus} onInspect={inspect} />
            )}
          />
        </TabsContent>

        <TabsContent value="story" className="mt-4">
          <StoryTab characterId={id} />
        </TabsContent>

        <TabsContent value="sources" className="mt-4 max-w-3xl">
          <SourcesTab characterId={id} />
        </TabsContent>

        <TabsContent value="build" className="mt-4">
          <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_24rem] xl:grid-cols-[minmax(0,1fr)_28rem] 3xl:grid-cols-[minmax(0,1fr)_36rem]">
            {/* min-w-0: a long pick must not stretch the column past a phone's screen */}
            <div className="min-w-0 space-y-4">
              {isLoading && <Spinner className="mx-auto my-12 size-6" />}
              {spellChoices.length > 0 && (
                <button
                  type="button"
                  onClick={() => setTab("magic")}
                  className={cn(
                    "flex w-full items-center gap-2 rounded-lg border px-4 py-3 text-left text-sm hover:bg-muted/60",
                    openSpells > 0 ? "border-amber-500/60 bg-amber-500/10" : "bg-background/60",
                  )}
                >
                  <SparklesIcon className="size-5 shrink-0 text-muted-foreground" />
                  <span className="min-w-0 flex-1">
                    <span className="font-medium">Spells</span>{" "}
                    {openSpells > 0 ? `${openSpells} to choose: on the Magic tab` : "all chosen: see or change them on the Magic tab"}
                  </span>
                  <span className="shrink-0 underline">Magic tab</span>
                </button>
              )}
              {sections
                .filter(([section]) => section !== "Extras")
                .map(([section, choices]) => (
                  <SectionCard key={section} section={section} choices={choices}>
                    {choices.map((choice) => (
                      <ChoiceRow
                        key={choice.ruleId}
                        characterId={id}
                        choice={choice}
                        hideLabel={choice.depth === 0 && choice.name === section}
                        onHighlight={setPreview}
                        onPicked={setFocus}
                        onInspect={inspect}
                      />
                    ))}
                  </SectionCard>
                ))}
              {!isLoading && (
                <ExtrasCard
                  characterId={id}
                  choices={sections.find(([section]) => section === "Extras")?.[1] ?? []}
                  renderChoice={(choice) => (
                    <ChoiceRow
                      key={choice.ruleId}
                      characterId={id}
                      choice={choice}
                      hideLabel={false}
                      onHighlight={setPreview}
                      onPicked={setFocus}
                      onInspect={inspect}
                    />
                  )}
                />
              )}
            </div>

            <aside className="hidden lg:block">
              <div className="sticky top-20 max-h-[calc(100vh-6rem)] overflow-y-auto rounded-lg border bg-background/60 p-5">
                <ElementPanel
                  id={preview ?? focus}
                  emptyHint="Open a list and point at an option to read about it here, or use the ⓘ button next to a pick."
                />
              </div>
            </aside>
          </div>
        </TabsContent>
      </Tabs>
      )}

      <Drawer open={drawerId !== null} onOpenChange={(open) => !open && setDrawerId(null)}>
        <DrawerContent>
          <DrawerHeader className="sr-only">
            <DrawerTitle>Description</DrawerTitle>
          </DrawerHeader>
          <div className="max-h-[75vh] overflow-y-auto px-4 pb-8">
            <ElementPanel id={drawerId} />
          </div>
        </DrawerContent>
      </Drawer>
    </div>
  );
}

function BuilderHeader({ characterId, choices, pending, guided }: { characterId: string; choices: BuilderChoice[]; pending: boolean; guided?: boolean }) {
  const { player } = usePlayer();
  const { data: headerData } = useCharacterHeader(characterId);
  const { data: classData } = useCharacterClassList(characterId);
  const setLevel = useSetClassLevel(characterId);
  const assignPlayer = useAssignPlayer(characterId);

  const character = headerData?.character;
  const primary = classData?.classes.find((c) => c.isPrimary) ?? classData?.classes[0];
  const pick = (section: string) => choices.find((c) => c.section === section && c.depth === 0)?.selected?.name;
  const summary = [pick("Species"), primary ? `${primary.name} ${primary.level}` : null, pick("Background")].filter(Boolean).join(" · ");
  const owner = character?.playerName;

  return (
    <header className="space-y-3">
      <Link to="/characters" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
        <ArrowLeftIcon className="size-4" /> Characters
      </Link>
      <div className="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
        <div className="flex min-w-0 items-center gap-4">
        <PortraitEditor characterId={characterId} url={character?.portraitUrl} />
        <div className="min-w-0">
          <h1 className="truncate font-heading text-3xl tracking-wide">{character?.name ?? "…"}</h1>
          <p className="text-muted-foreground">{summary || "Pick a class, species and background to get started."}</p>
          <p className="mt-1 flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
            <UserIcon className="size-4" />
            {owner ? `Player: ${owner}` : "No player"}
            {player && owner?.toLowerCase() !== player.toLowerCase() && (
              <Button variant="outline" size="sm" className="h-7" disabled={assignPlayer.isPending} onClick={() => assignPlayer.mutate(player)}>
                Move to {player}
              </Button>
            )}
          </p>
        </div>
        </div>

        <div className="flex flex-wrap items-center gap-3">
          {/* the step-by-step guide keeps the screen for the steps (its last step links to the sheet) */}
          {!guided && (
            <>
              <Button variant="outline" asChild>
                <Link to={`/characters/${characterId}/play`}>
                  <FileTextIcon /> Character sheet
                </Link>
              </Button>
              <AuroraUpdateDialog characterId={characterId} />
              <CharacterCampaigns characterId={characterId} playerName={owner} />
            </>
          )}
          {(pending || setLevel.isPending) && (
            <span className="flex items-center gap-2 text-sm text-muted-foreground">
              <Spinner className="size-4" /> Updating…
            </span>
          )}
          <ClassLevels characterId={characterId} classes={classData?.classes ?? []} busy={setLevel.isPending} onLevel={(characterClassId, newLevel) =>
            setLevel.mutate({ characterClassId, newLevel }, { onError: (e) => toast.error("Could not change the level", { description: e.message }) })
          } />
        </div>
      </div>
    </header>
  );
}

/**
 * The character's classes, each with its level (the total is the character's level, at most 20), and multiclassing:
 * add another class (its prerequisite shown; one the character does not meet is greyed out) or remove one added.
 */
function ClassLevels({
  characterId,
  classes,
  busy,
  onLevel,
}: {
  characterId: string;
  classes: { characterClassId: string; name: string; level: number; isPrimary: boolean }[];
  busy: boolean;
  onLevel: (characterClassId: string, newLevel: number) => void;
}) {
  const multiclass = useMulticlass(characterId);
  const add = useAddMulticlass(characterId);
  const remove = useRemoveMulticlass(characterId);
  const [adding, setAdding] = useState(false);
  const total = classes.reduce((n, c) => n + c.level, 0);
  const sorted = [...classes].sort((a, b) => Number(b.isPrimary) - Number(a.isPrimary));
  const takenOf = (name: string) => multiclass.data?.taken.find((t) => t.name === `${name} (multiclass)`);
  const options = [...(multiclass.data?.options ?? [])].sort((a, b) => Number(b.eligible) - Number(a.eligible) || a.name.localeCompare(b.name));
  return (
    <div className="flex flex-wrap items-center gap-2">
      {sorted.map((c) => {
        const taken = takenOf(c.name);
        return (
          <div key={c.characterClassId} className="flex items-center gap-1 rounded-lg border px-2 py-1">
            <Button variant="ghost" size="icon-sm" aria-label={`${c.name} level down`} disabled={c.level <= 1 || busy} onClick={() => onLevel(c.characterClassId, c.level - 1)}>
              <MinusIcon />
            </Button>
            <div className="min-w-16 text-center">
              <div className="text-xs uppercase tracking-widest text-muted-foreground">{classes.length > 1 ? c.name : "Level"}</div>
              <div className="font-heading text-xl">{c.level}</div>
            </div>
            <Button variant="ghost" size="icon-sm" aria-label={`${c.name} level up`} disabled={total >= 20 || busy} onClick={() => onLevel(c.characterClassId, c.level + 1)}>
              <PlusIcon />
            </Button>
            {taken && (
              <Button
                variant="ghost"
                size="icon-sm"
                aria-label={`Remove the ${c.name} levels`}
                title={`Remove the ${c.name} levels`}
                disabled={remove.isPending}
                onClick={() => remove.mutate(taken.extraId, { onError: (e) => toast.error("Could not remove the class", { description: e.message }) })}
              >
                ×
              </Button>
            )}
          </div>
        );
      })}
      {classes.length > 0 && (
        <Button variant="outline" size="sm" disabled={total >= 20} onClick={() => setAdding(true)}>
          <PlusIcon /> Add a class
        </Button>
      )}
      {classes.length > 1 && <span className="text-sm text-muted-foreground">Level {total}</span>}
      <Dialog open={adding} onOpenChange={setAdding}>
        <DialogContent className="max-h-[85dvh] overflow-y-auto sm:max-w-lg">
          <DialogHeader>
            <DialogTitle>Add a class (multiclass)</DialogTitle>
            <DialogDescription>
              The new class starts at level 1 and raises the character's level by one. You get its hit die and features, but only some of its starting proficiencies. You need the ability scores it asks for.
            </DialogDescription>
          </DialogHeader>
          {multiclass.isLoading ? (
            <Spinner className="mx-auto my-4 size-5" />
          ) : (
            <ul className="space-y-1.5">
              {options.map((o) => (
                <li key={o.elementId}>
                  <button
                    type="button"
                    disabled={!o.eligible || add.isPending}
                    onClick={() =>
                      add.mutate(o.elementId, {
                        onSuccess: () => {
                          setAdding(false);
                          toast.success(`${o.name} added at level 1`);
                        },
                        onError: (e) => toast.error("Could not add the class", { description: e.message }),
                      })
                    }
                    className={cn("flex w-full items-center justify-between gap-3 rounded-md border px-3 py-2 text-left", o.eligible ? "hover:bg-muted" : "cursor-not-allowed opacity-50")}
                  >
                    <span>
                      <span className="font-medium">{o.name}</span>
                      <span className="block text-xs text-muted-foreground">{o.source}</span>
                    </span>
                    <span className="text-right text-xs text-muted-foreground">{o.prerequisite ? `Needs ${o.prerequisite}` : ""}{!o.eligible && <span className="block text-destructive">not met</span>}</span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </DialogContent>
      </Dialog>
    </div>
  );
}

const TAB_NAMES: Record<BuilderTab, string> = { build: "Build", magic: "Magic", equipment: "Equipment" };

/**
 * What is still to choose, on top of the builder while it scrolls: how many on each tab, and a button that opens the
 * tab with the next one, scrolls to it and makes it flash. "Everything is chosen" when nothing is left.
 */
function ToDoBar({ open, loading, tab, setTab }: { open: Record<BuilderTab, number>; loading: boolean; tab: string; setTab: (t: string) => void }) {
  const next = useRef(0);
  const total = open.build + open.magic + open.equipment;
  if (loading) return null;
  if (total === 0) {
    return (
      <p className="mb-3 flex items-center gap-2 rounded-lg border border-emerald-500/40 bg-emerald-500/10 px-3 py-2 text-sm text-emerald-800 dark:text-emerald-200">
        <CircleCheckIcon className="size-4" /> Everything is chosen.
      </p>
    );
  }
  // each open choice of this tab once, then on to the next tab that has some
  function showNext() {
    const tabs = (["build", "magic", "equipment"] as BuilderTab[]).filter((t) => open[t] > 0);
    const go = () => {
      const rows = [...document.querySelectorAll<HTMLElement>('[role="tabpanel"][data-state="active"] [data-open-choice]')];
      if (rows.length === 0) return;
      const row = rows[next.current++ % rows.length];
      row.scrollIntoView({ behavior: "smooth", block: "center" });
      row.animate([{ boxShadow: "0 0 0 0 rgb(245 158 11 / 0.9)" }, { boxShadow: "0 0 0 10px rgb(245 158 11 / 0)" }], { duration: 900, iterations: 2 });
    };
    const here = tab as BuilderTab;
    if (tabs.includes(here) && next.current < open[here]) return go();
    const target = tabs[(tabs.indexOf(here) + 1) % tabs.length];
    next.current = 0;
    if (target === tab) return go();
    setTab(target);
    setTimeout(go, 250);
  }
  const parts = (["build", "magic", "equipment"] as BuilderTab[]).filter((t) => open[t] > 0).map((t) => `${TAB_NAMES[t]} ${open[t]}`);
  return (
    <div className="sticky top-16 z-20 mb-3 flex flex-wrap items-center gap-2 rounded-lg border border-amber-500/60 bg-background/95 px-3 py-2 shadow-md backdrop-blur">
      <CircleAlertIcon className="size-4 shrink-0 text-amber-500" />
      <span className="min-w-0 flex-1 text-sm">
        <span className="font-semibold">
          {total} {total === 1 ? "choice" : "choices"} still to make
        </span>
        <span className="text-muted-foreground"> · {parts.join(" · ")}</span>
      </span>
      <Button size="sm" className="bg-amber-500 text-black hover:bg-amber-400" onClick={showNext}>
        <ArrowDownIcon /> Show me the next one
      </Button>
    </div>
  );
}

function SectionCard({ section, choices, children }: { section: string; choices: BuilderChoice[]; children: ReactNode }) {
  const Icon = SECTION_ICONS[section] ?? SparklesIcon;
  const open = choices.filter((c) => !c.selected && !c.optional).length;

  return (
    <section className="rounded-lg border bg-background/60">
      <div className="ornament-section-head flex items-center gap-2 border-b px-4 py-3">
        <Icon className="size-5" />
        <h2 className="font-heading text-lg tracking-wide">{section}</h2>
        {open > 0 ? (
          <Badge variant="secondary" className="ms-auto">
            {open} to choose
          </Badge>
        ) : (
          choices.every((c) => c.optional && !c.selected) && (
            <Badge variant="outline" className="ms-auto">
              optional
            </Badge>
          )
        )}
      </div>
      <div className="space-y-3 p-4">{children}</div>
    </section>
  );
}

function ChoiceRow({
  characterId,
  choice,
  hideLabel,
  onHighlight,
  onPicked,
  onInspect,
}: {
  characterId: string;
  choice: BuilderChoice;
  hideLabel: boolean;
  onHighlight: (id: string | null) => void;
  onPicked: (id: string) => void;
  onInspect: (id: string) => void;
}) {
  const u = useUnits();
  const [open, setOpen] = useState(false);
  const optionsQuery = useChoiceOptions(characterId, choice.ruleId, open);
  const pickChoice = usePickChoice(characterId);
  const clearChoice = useClearChoice(characterId);
  const busy = pickChoice.isPending || clearChoice.isPending;

  const options = useMemo(
    // an option the character does not qualify for yet (a plan for a higher level) stays listed, greyed out, with
    // what it needs, so the player sees what later levels bring
    () =>
      optionsQuery.data?.options.map((o) => ({
        id: o.elementId,
        name: o.name,
        // a spell's line says what it is (level, school, concentration, ritual); filters and the details strip read the rest
        hint: [
          o.spell ? spellHint(o.spell) : null,
          o.eligible === false ? `Needs ${o.prerequisite || "something you don't have yet"}` : o.prerequisite,
          o.source,
        ]
          .filter(Boolean)
          .join(" · "),
        disabled: o.eligible === false,
        facets: o.spell ? spellFacets(o.spell) : undefined,
        kind: o.spell ? spellKind(o.spell) : null,
        details: o.spell ? spellDetails(o.spell).map((d) => (d.label === "Range" ? { ...d, value: u.label(d.value) } : d)) : undefined,
        needs: o.prerequisite ?? (o.eligible === false ? "something you don't have yet" : null),
      })),
    [optionsQuery.data, u],
  );
  const facets = useMemo(() => (optionsQuery.data?.options.some((o) => o.spell) ? SPELL_FACETS : undefined), [optionsQuery.data]);

  const meta = [
    choice.slots > 1 ? `${choice.slot} of ${choice.slots}` : null,
    choice.level > 0 ? `level ${choice.level}` : null,
    choice.depth > 0 && !choice.name.includes(choice.parentName) ? `from ${choice.parentName}` : null,
  ].filter(Boolean);

  if (choice.type === "Spell") return <SpellChoice characterId={characterId} choice={choice} />;

  // a choice still to make stands out (amber), so the player finds what is left; "Show me the next one" looks for it
  const todo = isOpen(choice);
  return (
    <div
      data-open-choice={todo || undefined}
      className={cn(
        "flex scroll-mt-40 flex-col gap-1.5 sm:flex-row sm:items-center sm:gap-4",
        choice.depth > 0 && "border-l-2 pl-3",
        todo && "rounded-md border-l-4 border-amber-500 bg-amber-500/10 py-2 pe-2 ring-1 ring-amber-500/50",
        todo && choice.depth === 0 && "ps-3",
      )}
      style={choice.depth > 1 ? { marginLeft: `${(choice.depth - 1) * 1}rem` } : undefined}
    >
      {!hideLabel && (
        <div className="min-w-0 shrink-0 sm:w-60">
          <div className="flex items-center gap-1.5 text-sm font-medium" title={choice.name}>
            <span className="truncate">{choice.name}</span>
            {todo && (
              <span className="inline-flex shrink-0 items-center gap-1 rounded-full bg-amber-500 px-1.5 text-[11px] font-semibold text-black">
                <span className="size-1.5 animate-pulse rounded-full bg-black/70" /> To choose
              </span>
            )}
          </div>
          {meta.length > 0 && <div className="truncate text-xs text-muted-foreground">{meta.join(" · ")}</div>}
        </div>
      )}
      <div className="flex min-w-0 flex-1 items-center gap-1">
        <SearchSelect
          value={choice.selected?.elementId}
          valueLabel={choice.selected?.name}
          valueHint={choice.selected?.source}
          options={options}
          facets={facets}
          loading={optionsQuery.isLoading}
          placeholder={`Choose ${placeholderOf(choice)}…`}
          disabled={busy}
          onOpenChange={(next) => {
            setOpen(next);
            if (!next) onHighlight(null);
          }}
          onHighlight={onHighlight}
          onSelect={(elementId) => {
            onPicked(elementId);
            pickChoice.mutate(
              { choice, elementId },
              { onError: (e) => toast.error(`Could not pick for ${choice.name}`, { description: e.message }) },
            );
          }}
          onClear={() =>
            clearChoice.mutate(choice, { onError: (e) => toast.error(`Could not clear ${choice.name}`, { description: e.message }) })
          }
        />
        {busy ? (
          <Spinner className="mx-2 size-4 shrink-0" />
        ) : (
          choice.selected && (
            <Button variant="ghost" size="icon-sm" className="shrink-0" aria-label={`About ${choice.selected.name}`} onClick={() => onInspect(choice.selected!.elementId)}>
              <InfoIcon />
            </Button>
          )
        )}
      </div>
    </div>
  );
}

function SourcesTab({ characterId }: { characterId: string }) {
  const { data: sourceData, isLoading } = useSources();
  const { data: characterSources } = useCharacterSources(characterId);
  const setSources = useSetCharacterSources(characterId);

  const save = (restricted: string[]) =>
    setSources.mutate(restricted, { onError: (e) => toast.error("Could not save the sources", { description: e.message }) });

  return (
    <div className="space-y-3">
      <p className="text-sm text-muted-foreground">Which rules does this character use?</p>
      <EditionPicker
        value={sourceData && characterSources ? editionOf(characterSources.restricted, sourceData.sources) : null}
        onChange={(edition) => sourceData && characterSources && save(switchEdition(edition, characterSources.restricted, sourceData.sources))}
      />
      <p className="pt-3 text-sm text-muted-foreground">
        Or tick the books yourself. The dropdowns only offer content from ticked books; picks you already made stay.
      </p>
      <SourcesPicker
        sources={sourceData?.sources}
        restricted={characterSources?.restricted ?? []}
        loading={isLoading || !characterSources}
        onChange={save}
      />
    </div>
  );
}

/** The character's portrait: drop a picture on it or click it to choose one (shrunk in the browser first). */
function PortraitEditor({ characterId, url }: { characterId: string; url?: string | null }) {
  const upload = usePortraitUpload(characterId);
  const remove = useRemovePortrait(characterId);
  const input = useRef<HTMLInputElement>(null);
  const busy = upload.isPending || remove.isPending;
  const { over, handlers } = usePictureDrop(upload.send);

  return (
    <div
      {...handlers}
      className={cn(
        "group relative size-24 shrink-0 overflow-hidden rounded-xl border-4 border-double bg-muted sm:size-28",
        over && "border-primary ring-4 ring-primary/40",
      )}
    >
      {url ? (
        <img src={url} alt="Portrait" className="size-full object-cover" />
      ) : (
        <button
          type="button"
          onClick={() => input.current?.click()}
          className="flex size-full flex-col items-center justify-center gap-1 px-1 text-center text-xs text-muted-foreground hover:text-foreground"
        >
          <ImageIcon className="size-6" />
          Add portrait
          <span className="text-[11px] leading-tight">drop a picture or click</span>
        </button>
      )}
      {url && (
        // on a phone two icons a finger can hit (the words did not fit under the picture); on a computer on hover
        <div className="absolute inset-x-0 bottom-0 flex justify-center gap-2 bg-black/70 py-0.5 text-xs text-white sm:py-1 sm:opacity-0 sm:transition-opacity sm:group-hover:opacity-100">
          <button type="button" aria-label="Change the portrait" onClick={() => input.current?.click()} className="flex min-h-9 min-w-9 items-center justify-center hover:underline sm:min-h-0 sm:min-w-0">
            <ImageIcon className="size-4 sm:hidden" />
            <span className="max-sm:sr-only">Change</span>
          </button>
          <button type="button" aria-label="Remove the portrait" onClick={() => remove.mutate()} className="flex min-h-9 min-w-9 items-center justify-center hover:underline sm:min-h-0 sm:min-w-0">
            <Trash2Icon className="size-4 sm:hidden" />
            <span className="max-sm:sr-only">Remove</span>
          </button>
        </div>
      )}
      {over && <div className="absolute inset-0 flex items-center justify-center bg-primary/30 text-xs font-medium text-white">Drop it</div>}
      {busy && (
        <div className="absolute inset-0 flex items-center justify-center bg-black/50">
          <Spinner className="size-5 text-white" />
        </div>
      )}
      <input
        ref={input}
        type="file"
        accept="image/*"
        hidden
        onChange={(e) => {
          const file = firstImage(e.target.files);
          e.target.value = "";
          if (file) upload.send(file);
        }}
      />
    </div>
  );
}

/** What a choice asks for, in words: its kind ("proficiency", "sub race"), or its own name when the kind says nothing ("a list"). */
function placeholderOf(choice: BuilderChoice): string {
  if (/^Improvement Option\b/i.test(choice.name)) return "ability score improvement or feat";
  const generic = /^(list|option|grants|information|support)$/i.test(choice.type);
  return (generic ? choice.name.replace(/\s*\([^)]*\)\s*$/, "") : choice.type).toLowerCase();
}
