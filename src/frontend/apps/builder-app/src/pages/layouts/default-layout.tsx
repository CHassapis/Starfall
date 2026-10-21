import { Button } from "@/components/ui/button";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { Badge } from "@/components/ui/badge";
import { Separator } from "@/components/ui/separator";
import { cn } from "@/lib/utils";
import { BookOpenIcon, CrownIcon, DatabaseIcon, FlaskConicalIcon, LibraryBigIcon, MenuIcon, ScrollTextIcon, SwordsIcon, UserIcon } from "lucide-react";
import { Link, NavLink, Outlet } from "react-router-dom";
import { ModeToggle } from "@/components/mode-toggle";
import { GitHubIconButton } from "@/components/navigation/github-icon-button";
import { useIsAdmin, usePlayer } from "@/lib/player";
import { AdminButton } from "@/components/admin-button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { PlayerChooser } from "@/pages/characters/player-picker";
import { useState } from "react";

export function LandingBackground() {
  return (
    <>
      <div
        className="absolute inset-0 -z-10 pointer-events-none bg-background dark:hidden"
        style={{
          backgroundImage: `
        linear-gradient(45deg, transparent 49%, #e5e7eb 49%, #e5e7eb 51%, transparent 51%),
        linear-gradient(-45deg, transparent 49%, #e5e7eb 49%, #e5e7eb 51%, transparent 51%)
            `,
          backgroundSize: "40px 40px",
          WebkitMaskImage: "radial-gradient(ellipse 70% 60% at 50% 0%, #000 60%, transparent 100%)",
          maskImage: "radial-gradient(ellipse 70% 60% at 50% 0%, #000 60%, transparent 100%)",
        }}
      />
      <div
        className="absolute inset-0 -z-10 pointer-events-none bg-background hidden dark:block"
        style={{
          backgroundImage: `
        linear-gradient(45deg, transparent 49%, rgba(255,255,255,0.06) 49%, rgba(255,255,255,0.06) 51%, transparent 51%),
        linear-gradient(-45deg, transparent 49%, rgba(255,255,255,0.04) 49%, rgba(255,255,255,0.04) 51%, transparent 51%)
            `,
          backgroundSize: "40px 40px",
          WebkitMaskImage: "radial-gradient(ellipse 70% 60% at 50% 0%, #000 60%, transparent 100%)",
          maskImage: "radial-gradient(ellipse 70% 60% at 50% 0%, #000 60%, transparent 100%)",
        }}
      />
    </>
  );
}

export function LandingBackground2() {
  return (
    <>
      <div
        className="absolute inset-0 -z-10 pointer-events-none bg-background dark:hidden"
        style={{
          backgroundImage: `
        linear-gradient(45deg, transparent 49%, #e5e7eb 49%, #e5e7eb 51%, transparent 51%),
        linear-gradient(-45deg, transparent 49%, #e5e7eb 49%, #e5e7eb 51%, transparent 51%)
            `,
          backgroundSize: "40px 40px",
          WebkitMaskImage: "radial-gradient(ellipse 70% 60% at 50% 0%, #000 60%, transparent 100%)",
          maskImage: "radial-gradient(ellipse 70% 60% at 50% 0%, #000 60%, transparent 100%)",
        }}
      />
      <div
        className="absolute inset-0 -z-10 pointer-events-none bg-background hidden dark:block"
        style={{
          backgroundImage: `
        linear-gradient(45deg, transparent 49%, rgba(255,255,255,0.06) 49%, rgba(255,255,255,0.06) 51%, transparent 51%),
        linear-gradient(-45deg, transparent 49%, rgba(255,255,255,0.04) 49%, rgba(255,255,255,0.04) 51%, transparent 51%)
            `,
          backgroundSize: "40px 40px",
          WebkitMaskImage: "radial-gradient(ellipse 70% 60% at 50% 0%, #000 60%, transparent 100%)",
          maskImage: "radial-gradient(ellipse 70% 60% at 50% 0%, #000 60%, transparent 100%)",
        }}
      />
    </>
  );
}

export function LandingBackground3() { return null; }

function SizeIndicatorBadge({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div className={cn("uppercase", className)} {...props}>
      <Badge variant={"default"} className="bg-starlights-development block sm:hidden">
        xs
      </Badge>
      <Badge variant={"default"} className="bg-starlights-development hidden sm:block md:hidden">
        sm
      </Badge>
      <Badge variant={"default"} className="bg-starlights-development hidden md:block lg:hidden">
        md
      </Badge>
      <Badge variant={"default"} className="bg-starlights-development hidden lg:block xl:hidden ">
        lg
      </Badge>
      <Badge variant={"default"} className="bg-starlights-development hidden xl:block 2xl:hidden">
        xl
      </Badge>
      <Badge variant={"default"} className="bg-starlights-development hidden 2xl:block">
        2xl
      </Badge>
    </div>
  );
}

export function MainNavigation() {
  return (
    <>
      <nav className="flex items-center justify-between h-16 ">
        <div className="flex min-w-0 items-center justify-start gap-2 ">
          {/* the home button never gives up its room: the links shrink to icons first */}
          <Link to="/" aria-label="Starfall home" className="flex shrink-0 items-center gap-2.5 font-heading relative">
            <img src="/icons/icon-192.png" alt="" width={44} height={44} className="size-11 shrink-0 rounded-lg border border-[color-mix(in_srgb,var(--ornament)_45%,var(--border))]" />
            <span className="hidden xl:inline text-sm font-semibold uppercase tracking-[0.22em] mt-0.5">Starfall</span>
          </Link>

          <MainLinks className="ms-2 hidden lg:flex" />
        </div>

        <div className="flex items-center justify-end gap-2">
          {/* phones: the same places as icons */}
          <MobileNavigation />
          <PlayerChip />
          <AdminButton />
          <div className="flex items-center justify-end gap-2">
            <ModeToggle />
            {/* room for the menu icons on a phone */}
            <span className="hidden sm:contents">
              <GitHubIconButton />
            </span>
          </div>

          {import.meta.env.DEV && (
            <>
              <Separator orientation="vertical" className="hidden min-h-5 xl:block" />
              <SizeIndicatorBadge className="ms-2 hidden xl:block" />
            </>
          )}

          {/* <div className="flex items-center justify-end gap-2">
            <AppMenuComponent />
          </div> */}
        </div>
      </nav>
    </>
  );
}

/** The app's main places, as plain links (icons only on a phone); the current one is highlighted. */
const MAIN_LINKS = [
  { to: "/characters", label: "Characters", icon: CrownIcon },
  { to: "/campaigns", label: "Campaigns", icon: ScrollTextIcon },
  { to: "/battle", label: "Battle", icon: SwordsIcon },
  { to: "/lore", label: "Lore", icon: LibraryBigIcon },
  { to: "/compendium", label: "Compendium", icon: BookOpenIcon },
  { to: "/homebrew", label: "Homebrew", icon: FlaskConicalIcon },
];

function MobileNavigation() {
  const admin = useIsAdmin();
  const links = admin ? [...MAIN_LINKS, { to: "/content", label: "Content", icon: DatabaseIcon }] : MAIN_LINKS;
  return <div className="lg:hidden"><DropdownMenu><DropdownMenuTrigger asChild><Button variant="outline" aria-label="Open navigation"><MenuIcon /><span className="hidden min-[390px]:inline">Menu</span></Button></DropdownMenuTrigger><DropdownMenuContent align="end">{links.map(({ to, label, icon: Icon }) => <DropdownMenuItem key={to} asChild><Link to={to} className="min-h-11"><Icon />{label}</Link></DropdownMenuItem>)}</DropdownMenuContent></DropdownMenu></div>;
}

function MainLinks({ className, compact }: { className?: string; compact?: boolean }) {
  // the admin also gets the Content page (where the rules and the Compendium come from)
  const links = useIsAdmin() ? [...MAIN_LINKS, { to: "/content", label: "Content", icon: DatabaseIcon }] : MAIN_LINKS;
  return (
    <nav aria-label="Main" className={cn("items-center gap-0.5", compact ? "flex" : "", className)}>
      {links.map(({ to, label, icon: LinkIcon }) => (
        <NavLink
          key={to}
          to={to}
          aria-label={label}
          title={label}
          className={({ isActive }) =>
            cn(
              "flex items-center gap-1.5 rounded-md text-sm transition-colors hover:bg-muted hover:text-foreground",
              compact ? "p-2" : "px-2.5 py-1.5",
              isActive ? "text-foreground bg-muted/60 nav-active" : "text-muted-foreground",
              compact && (to === "/compendium" || to === "/homebrew" || to === "/content") && "hidden sm:flex",
            )
          }
        >
          <LinkIcon className={compact ? "size-5" : "size-4"} />
          {!compact && <span className={cn(to === "/lore" || to === "/compendium" || to === "/homebrew" || to === "/content" ? "hidden xl:inline" : "")}>{label}</span>}
        </NavLink>
      ))}
    </nav>
  );
}

/** Who is playing, in the top bar: opens "Who's playing?" to switch to another player or add a new one. */
function PlayerChip() {
  const { player } = usePlayer();
  const [open, setOpen] = useState(false);
  return (
    <>
      <button
        type="button"
        onClick={() => setOpen(true)}
        title={player ? `Playing as ${player}: change player` : "Who's playing?"}
        aria-label={player ? `Playing as ${player}: change player` : "Who's playing?"}
        className="flex items-center gap-1.5 rounded-md border p-2 text-xs hover:bg-muted sm:px-2 sm:py-1"
      >
        <UserIcon className="size-4 sm:size-3.5" />
        <span className="hidden max-w-36 truncate sm:inline">{player ?? "Who's playing?"}</span>
      </button>
      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle className="font-heading text-2xl tracking-wide">Who's playing?</DialogTitle>
            <DialogDescription>Switch to another player, or add a new one. This browser remembers the choice.</DialogDescription>
          </DialogHeader>
          <PlayerChooser onChosen={() => setOpen(false)} />
        </DialogContent>
      </Dialog>
    </>
  );
}

export function Header() {
  return (
    <div className="app-header sticky top-0 bg-background/80 backdrop-blur-md z-20 border-b print:hidden">
      <header className="px-4 md:px-6 xl:px-10">
        <MainNavigation />
      </header>
    </div>
  );
}

export function CharactersLayout() {
  return (
    <>
      <Header />
      {/* <div className="border-b border-b-slate-200/50 dark:border-b-slate-700/50">
        <NavigationMenuDemo />
      </div> */}

      <div className="">
        <div className="">
          <LandingBackground2 />
          <Outlet />
        </div>
      </div>
    </>
  );
}
