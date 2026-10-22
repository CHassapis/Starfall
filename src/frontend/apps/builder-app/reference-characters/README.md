# Reference characters

Ten synthetic characters whose figures were worked out by hand from the rules, used by both test suites:

- the server suite (`src/tests/integration/Starlights.Integration.Tests/Tests/Calculation/ReferenceCharacterTests.cs`)
  imports `content/` into an in-memory database, builds each character through the API step by step, and checks
  the server's figures, statistics, spellcasting and calculation traces;
- the web suite (`src/lib/reference-characters.test.ts`) feeds the same characters' server figures and inventory
  to the sheet's calculations and checks the sheet, the PDF fields, the battle view and their traces.

Nothing here is book text or anyone's character. `content/` is invented Aurora-style content (names start with
"Ref", ids with `ID_REF`), shaped like Aurora's rules so the same code paths run; the few ids that must match
Aurora's own (`ID_PROFICIENCY_WEAPON_PROFICIENCY_SIMPLE_WEAPONS`, `…_MARTIAL_WEAPONS`) are named because the
sheet looks for them.

## The rule for expectations

Every expected figure in `characters/*.json` is worked out by hand, and its `why` shows the arithmetic. **Never
generate or update an expectation from Starfall's output, and never change one to make a test pass.** If a test
fails, either Starfall is wrong (fix it, with a focused regression test) or the hand arithmetic is wrong (fix it
and say why in the commit message, with the rule it follows).

## Rules the arithmetic follows

- Ability modifier: `floor((score - 10) / 2)`. Proficiency bonus: +2 at levels 1–4, +3 at 5–8.
- Hit points: the first class's full die at level 1, then each level the die's average (`die / 2 + 1`), plus the
  Constitution modifier every level, plus bonuses (`hp`). Rolled hit points (Aurora's `rndhp`, the story's
  `hitPointRolls`) replace the dice when there is one for every level.
- The same kind of bonus (Aurora's `bonus` name) counts once: the highest. Two skill proficiencies from a class and
  a background, or two "warding" items, add once.
- An item that sets a score (`strength:score:set`) gives that score when it is higher than what the character has.
- A score is capped at 20 unless the base is already higher.
- 2014 half proficiency (a bard's) rounds down; it applies to ability checks and initiative, not saving throws, and
  not to skills the character is proficient in.
- Multiclass spell slots: full casters count their whole level, half casters half of it, rounded **down in 2014**
  and **up in 2024** (c06 and c07 tell the two apart).
- A second class gives none of the first class's saving throws (`!ID_REF_MULTICLASS_*` requirements).
- Armor class: worn armor (light: all of Dexterity, medium: at most +2, heavy: none) or the best unarmored
  calculation, plus a shield, plus magic and other bonuses. Finesse weapons use the better of Strength and
  Dexterity; ranged weapons use Dexterity.

## What the reference content leaves out on purpose

- The reference monk has no Martial Arts: its quarterstaff uses Strength like anyone's.
- Requirements, levels and "while wearing" conditions are applied when the character's elements are worked out,
  before the figures are calculated. A trace shows the result (a rule that is not there), and reports a condition
  as an exclusion only where the code that applies it is the one producing the trace (the sheet's armor class).

## The characters

| File | Covers |
|---|---|
| c01-fighter-2014 | armor and shield, fighting style while armored, level 5 (proficiency +3, Extra Attack), taking armor off |
| c02-fighter-2014-asi | Ability Score Improvement past 20 (capped), magic armor, magic weapon two-handed |
| c03-rogue-2014 | expertise, the same skill twice, finesse, a weapon's own proficiency, export and import |
| c04-bard-2014 | half proficiency, spell attack and DC with an item bonus, hit points per level, removing an attuned item |
| c05-wizard-2014 | an item setting Strength, bonuses that do not stack, an item bonus to one save |
| c06-paladin-wizard-2014 | multiclass, 2014 half-caster rounding, two spellcastings |
| c07-ranger-cleric-2024 | multiclass, 2024 half-caster rounding, medium armor's Dexterity cap, archery, initiative feat |
| c08-monk-2024 | an unarmored defense needing no shield, speed while unarmored, picking up a shield |
| c09-barbarian-2024 | two unarmored defenses competing (and tying), Extra Attack, d12 hit points |
| c10-imported-aurora-rogue | what the Aurora import produces from `aurora/imported-rogue.dnd5e`: rolled hit points, a hand-written attack |

Each spec lists its build `steps` (what a player does in the builder, in an order the builder allows: it offers
no skill the character already has), the `expected` figures, and `scenarios`. Scenarios run one after another on
the same character (a level-up, armor taken off, an item un-attuned, an export and import); each lists what it
changes, merged over the expectations before it (abilities, saving throws, skills, statistics and spellcastings one
by one; anything else replaced), and the merged expectations are checked in full.

`traces` name values whose trace is checked: its `final`, its own exclusions (`excluded`: reason and value of each
value that was considered for it and left out; the exclusions of the values it is made from are in their own traces),
and `operations` that must appear in it or the traces of what it is made from. Alternatives the calculation weighed and
did not take are exclusions too (`lower-alternative`: Strength for a finesse weapon when Dexterity is higher, 10 +
Dexterity when an unarmored defense is higher; `condition-not-met`: a monk's defense with a shield). Server keys are
checked by the server suite, the sheet's own keys (armor-class, hit-points, attack:…) by the web suite.

Two lists must be complete, because the web suite works from the file alone:

- `statistics`: every statistic the sheet reads (armor class parts, speeds, initiative, hit point bonuses,
  attack and damage bonuses, spell attack and DC bonuses) that the character has; `absentStatistics` names ones
  that must not be there;
- `registered`: every registered element the sheet's calculations look for (weapon proficiencies, Extra Attack).

The server suite fails when the server has one the file does not list. `catalog.json` lists the reference items
with the fields the sheet needs; the server suite checks that the server's item catalog agrees with it.

The server suite needs a disposable SQL Server (saving an inventory takes a row lock): set `STARFALL_TEST_SQLSERVER`
as for the other `SqlServer` tests. CI does.
