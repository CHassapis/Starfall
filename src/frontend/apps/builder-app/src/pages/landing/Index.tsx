import { Badge } from "@/components/ui/badge";
import { Link } from "react-router-dom";
import { BookOpenIcon, ChevronRightIcon, SwordsIcon, UsersIcon } from "lucide-react";
import { HourlyBackdrop } from "@/components/hourly-backdrop";
import { BATTLE_BACKGROUND, HOMEBREW_TILE, LORE_BACKGROUND } from "@/lib/art";
import { OrnamentalCorner } from "@/components/ornaments";

function LandingTile({
  title,
  description,
  url,
  image,
  imagePosition = "object-center",
  size = "lg",
  tag = undefined,
  enabled = true,
}: {
  title: string;
  description: string;
  url: string;
  image?: string;
  /** which part of the picture stays in view when the tile crops it */
  imagePosition?: "object-center" | "object-top";
  size?: "sm" | "lg";
  tag?: string;
  enabled?: boolean;
}) {
  const isLarge = size === "lg";

  return (
    <div className="relative h-full">
      <Link
        to={url}
        className={`ornament-hover block h-full relative overflow-hidden rounded-[0.7rem] group border border-[var(--ornament-line)] bg-[#101827] shadow-sm ${enabled ? "" : "pointer-events-none"} `}
      >
        {image ? (
          <img
            src={image}
            alt=""
            onError={(event) => { event.currentTarget.hidden = true; }}
            className={`absolute inset-0 w-full h-full object-cover ${imagePosition} transition-transform duration-500 scale-100 group-hover:scale-105 ${
              enabled ? "" : "grayscale-85 group-hover:grayscale-0 "
            }`}
          />
        ) : (
          <div className="absolute inset-0 bg-linear-to-br from-muted to-background" />
        )}

        <div className="absolute inset-0 bg-linear-to-t from-black/85 via-black/35 via-45% group-hover:from-black/75 to-transparent" />
        {/* 
        <div className="absolute rounded-lg rotate-160 w-20 h-20 -top-7 -right-5 bg-accent shadow-2xl"></div>
        <SwordsIcon className="absolute  top-1 right-4 size-6 rotate-0 opacity-20 mt-2.5 ms-2.5" /> */}

        <div
          className={`prose prose-neutral dark:prose-invert absolute text-white ${
            isLarge ? "left-2 right-2 bottom-2 sm:left-8 sm:right-8 sm:bottom-8" : "left-2 right-2 bottom-2 sm:left-4 sm:right-4 sm:bottom-4"
          }  `}
        >
          {isLarge ? (
            <>
              <h3 className="text-lg! sm:text-2xl! lg:text-[1.7rem]! uppercase tracking-wide text-white mb-0">{title}</h3>
              <p className="text-sm sm:text-base text-white/85 mb-0 max-w-xl pe-8">{description}</p>
            </>
          ) : (
            <>
              <h3 className="text-sm sm:text-base! lg:text-lg! uppercase tracking-wide text-white mb-0 mt-1 leading-tight">{title}</h3>
              <p className="text-xs sm:text-sm text-white/85 mb-0 pe-6">{description}</p>
            </>
          )}
        </div>
        <ChevronRightIcon aria-hidden className={`absolute text-white/80 transition-transform group-hover:translate-x-0.5 ${isLarge ? "right-3 bottom-3 size-6 sm:right-6 sm:bottom-7" : "right-2 bottom-2 size-5 sm:right-3 sm:bottom-3"}`} />
        <div className={` absolute ${isLarge ? "left-2 right-2 top-2 sm:left-8 sm:right-8 sm:top-8" : "left-2 top-2 sm:left-4 sm:right-4 sm:top-4"}  `}>
          {tag === undefined ? null : (
            <>
              <Badge variant={"outline"} className="backdrop-blur text-white border-white/50">
                {tag}
              </Badge>
            </>
          )}
        </div>
      </Link>
      {/* one subtle accent per tile: the feature tile also gets the opposite corner */}
      <OrnamentalCorner corner="tl" />
      {isLarge && <OrnamentalCorner corner="br" />}
    </div>
  );
}

const tiles = [
  {
    title: "Character Builder",
    description: "Craft and chronicle your adventurers with every Aurora book, 2014 and 2024 rules side by side.",
    url: "/characters",
    image: "/images/spiritdragon_olivierbernard_full.jpg",
  },
  {
    title: "Battle Action Simulator",
    description: "Pick a character and see everything it can do this turn, with real numbers.",
    url: "/battle",
    image: BATTLE_BACKGROUND,
  },
  {
    title: "Campaign Ledger",
    description: "Quests, NPCs, sessions, maps and loot for the whole table.",
    url: "/campaigns",
    image: "/images/drow.jpg",
  },
  {
    title: "Compendium of Lore",
    description: "Spells, items, monsters and every book, searchable and linked together.",
    url: "/lore",
    image: LORE_BACKGROUND,
  },
  {
    title: "Homebrew",
    description: "Make monsters, magic items, classes, subclasses and spells for your table.",
    url: "/homebrew",
    image: HOMEBREW_TILE,
    imagePosition: "object-top" as const,
  },
];

const SUPPORT = [
  { title: "Characters", icon: UsersIcon, text: "Every Aurora book and Aurora Legacy's newest content; the sheet prints exactly like Aurora's." },
  { title: "At the table", icon: SwordsIcon, text: "Campaigns with a locked DM side, player notes, and the Battle Action Simulator for each turn." },
  { title: "Reference", icon: BookOpenIcon, text: "The Compendium of Lore with every rule, monster and book, linked from wherever you are." },
];

export function LandingPage2() {
  return (
    <div className="landing-page pb-4 [--ornament-corner-size:3.25rem]">
      <HourlyBackdrop />
      {/* the brand block (the supplied D20 emblem over the live wordmark) beside the introduction */}
      <header className="mb-6 grid items-center gap-5 sm:grid-cols-[15rem_1fr] sm:gap-8 lg:mb-7">
        <h1 className="starfall-brand mx-auto w-full max-w-[15rem]">
          <span aria-hidden className="starfall-emblem" />
          <span className="starfall-wordmark">Starfall</span>
        </h1>
        <div className="max-w-2xl text-center sm:text-left">
          <p className="fantasy-eyebrow justify-center sm:justify-start" data-rule>Your table's companion</p>
          <p className="mt-3 text-base text-muted-foreground sm:text-lg">Build characters, run campaigns, look anything up and play out every turn of a fight, all in one place.</p>
        </div>
      </header>

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4 lg:grid-rows-2 lg:h-[31rem] 4xl:h-[38rem]">
        <div className="h-72 sm:col-span-2 lg:row-span-2 lg:h-auto">
          <LandingTile {...tiles[0]} />
        </div>
        <div className="h-44 lg:h-auto">
          <LandingTile {...tiles[1]} size="sm" />
        </div>
        <div className="h-44 lg:h-auto">
          <LandingTile {...tiles[4]} size="sm" />
        </div>
        <div className="h-44 lg:h-auto">
          <LandingTile {...tiles[2]} size="sm" />
        </div>
        <div className="h-44 lg:h-auto">
          <LandingTile {...tiles[3]} size="sm" />
        </div>
      </div>

      <section aria-label="What Starfall does" className="mt-4 grid gap-4 sm:grid-cols-3">
        {SUPPORT.map(({ title, icon: Icon, text }) => (
          <div key={title} className="flex items-start gap-3 rounded-[0.7rem] border border-[var(--ornament-faint)] bg-card/50 px-4 py-4 lg:gap-4 lg:px-5">
            <Icon aria-hidden strokeWidth={1.4} className="mt-0.5 size-7 shrink-0 text-[var(--ornament)] lg:size-9" />
            <div>
              <h2 className="text-xs font-semibold uppercase tracking-[0.2em] text-foreground">{title}</h2>
              <p className="mt-1.5 text-sm text-muted-foreground">{text}</p>
            </div>
          </div>
        ))}
      </section>
    </div>
  );
}
