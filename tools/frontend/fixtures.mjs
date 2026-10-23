/**
 * Synthetic, in-memory API fixture. No books, real characters, tokens or production requests. `large`: a character
 * with many spells and features, all with long made-up texts (for measuring the PDF sheet); the default is unchanged.
 */
export const CHARACTER_ID = "11111111-1111-4111-8111-111111111111";
// made-up filler text: a long description of nothing in particular
const filler = (name, sentences) =>
  Array.from({ length: sentences }, (_, i) => `${name} hums with made-up energy number ${i + 1}, and the testing light bends around it in a pattern no book describes.`).join(" ");
export function fixture({ large = false } = {}) {
  const base = `/api/characters/${CHARACTER_ID}`;
  const sources = ["Player's Handbook", "Player's Handbook (2024)", "Test Supplement (2024)"];
  const spells = Array.from({ length: large ? 72 : 12 }, (_, i) => ({
    id: `spell-${i}`,
    elementId: `spell-${i}`,
    auroraId: `ID_WOTC_PHB24_SPELL_TEST_${i}`,
    name: ["Arc Light", "Beacon", "Circle of Shelter", "Drift", "Echo", "Feather", "Glimmer", "Haven", "Illumination", "Jolt", "Kindle", "Lantern"][i] ?? `Test Spell ${i + 1}`,
    source: sources[1],
    level: i < 2 ? 0 : large ? 1 + (i % 9) : 1,
    school: i % 2 ? "Evocation" : "Abjuration",
    castingTime: "1 action",
    range: "60 feet",
    duration: "1 minute",
    components: "V, S",
    ritual: i === 2,
    concentration: i === 2,
    lists: ["Wizard"],
  }));
  const options = spells.map((s) => ({
    elementId: s.id,
    name: s.name,
    source: s.source,
    eligible: s.id !== "spell-11",
    prerequisite: s.id === "spell-11" ? "Wizard level 5" : null,
    spell: s,
  }));
  options.push({ ...options[2], elementId: "spell-reprint", source: sources[2] });
  const choices = [1, 2].map((n) => ({
    ruleId: `choice-${n}`,
    registrationId: "casting-reg",
    section: "Class",
    depth: 1,
    name: "Spellbook selection",
    type: "Spell",
    parentName: "Wizard Spellcasting",
    parentType: "Class Feature",
    parentElementId: "casting",
    level: n,
    slot: n,
    slots: 2,
    optional: false,
    selected: null,
  }));
  const caster = {
    name: "Wizard",
    className: "Wizard",
    classLevel: 3,
    edition: "2024",
    ability: "Intelligence",
    abilityModifier: 3,
    attack: 5,
    dc: 13,
    attackBonus: 0,
    dcBonus: 0,
    prepares: true,
    knowsWholeList: false,
    spellbook: true,
    allowReplace: false,
    lists: ["Wizard"],
    multiclass: "Full",
    slots: { 1: 4, 2: 2 },
    pact: null,
    prepareMax: 4,
    spells: spells
      .slice(0, large ? spells.length : 6)
      .map((s) => ({
        registrationId: `reg-${s.id}`,
        elementId: s.id,
        name: s.name,
        level: s.level,
        kind: s.level === 0 ? "cantrip" : "spellbook",
        origin: "Wizard",
      })),
    preparable: spells.slice(2, 6).map((s) => s.id),
    nextLevel: { level: 4, slots: { 1: 4, 2: 3 }, prepare: 5, choices: ["Two spells for your spellbook"] },
  };
  let magic = { version: 1, prepared: { Wizard: ["spell-2"] }, expendedSlots: { 1: 1 }, expendedPactSlots: 0 };
  let combat = {
    version: 1,
    damage: 3,
    temporaryHitPoints: 0,
    hitDiceSpent: 0,
    uses: {},
    concentration: null,
    conditions: [],
    exhaustion: 0,
    deathSaveSuccesses: 0,
    deathSaveFailures: 0,
    heroicInspiration: false,
    companions: [],
    active: [],
    effects: [],
  };
  let inventory = { version: 1, items: [], coins: { gp: 25 }, treasure: "", questItems: "" };
  let changed = false;
  const character = { characterId: CHARACTER_ID, name: "Mira Vale", level: 3, build: "Wizard", playerName: "Test Player", isFavorite: false };
  const abilities = ["Strength", "Dexterity", "Constitution", "Intelligence", "Wisdom", "Charisma"].map((name, i) => ({
    id: name,
    abilityScoreId: name,
    name,
    abbreviation: name.slice(0, 3).toUpperCase(),
    baseScore: i === 3 ? 16 : 12,
    additionalScore: 0,
    calculatedScore: i === 3 ? 16 : 12,
    calculatedModifier: i === 3 ? 3 : 1,
  }));
  const entries = [
    {
      id: "wizard",
      name: "Wizard",
      type: "Class",
      source: sources[1],
      setters: { hitDie: "d6", hd: "6" },
      sheet: null,
      spellcasting: null,
      description: "A synthetic test class.",
    },
    {
      id: "casting",
      name: "Spellcasting",
      type: "Class Feature",
      source: sources[1],
      setters: {},
      sheet: null,
      spellcasting: { name: "Wizard", ability: "Intelligence" },
      description: "Prepare spells from your spellbook.",
    },
    ...spells.map((s) => ({
      ...s,
      type: "Spell",
      setters: {
        level: String(s.level),
        school: s.school,
        time: s.castingTime,
        range: s.range,
        duration: s.duration,
        hasVerbalComponent: "true",
        hasSomaticComponent: "true",
        isRitual: String(s.ritual),
        isConcentration: String(s.concentration),
      },
      supports: s.lists,
      stats: [],
      sheet: null,
      spellcasting: null,
      description: large
        ? `<p>${s.name} is a synthetic testing spell. ${filler(s.name, 8)}</p><p>${filler(s.name, 6)}</p>`
        : `<p>${s.name} is a synthetic testing spell. No copyrighted rules text.</p>`,
    })),
  ];
  // the large character's features: long texts that run over several pages
  const features = large
    ? Array.from({ length: 40 }, (_, i) => ({
        id: `feature-${i}`,
        name: `Test Feature ${i + 1}`,
        type: "Class Feature",
        source: sources[1],
        setters: {},
        sheet: { display: true, alt: null, action: null, usage: i % 4 === 0 ? "2/Long Rest" : null, descriptions: [] },
        spellcasting: null,
        description: `<p>${filler(`Test Feature ${i + 1}`, 10)}</p>`,
      }))
    : [];
  entries.push(...features);
  entries.push({ ...entries.find(e => e.id === "spell-2"), id: "spell-reprint", source: sources[2] });
  const registrations = [
    {
      registrationId: "class-reg",
      name: "Wizard",
      type: "Class",
      associatedElementId: "wizard",
      children: [
        {
          registrationId: "casting-reg",
          parentRegistrationId: "class-reg",
          name: "Spellcasting",
          type: "Class Feature",
          associatedElementId: "casting",
          children: spells
            .slice(0, large ? spells.length : 6)
            .map((s) => ({ registrationId: `reg-${s.id}`, parentRegistrationId: "casting-reg", name: s.name, type: "Spell", associatedElementId: s.id })),
        },
        ...features.map((f) => ({ registrationId: `reg-${f.id}`, parentRegistrationId: "class-reg", name: f.name, type: "Class Feature", associatedElementId: f.id })),
      ],
    },
  ];
  // GET …/calculation/trace: the server's traces of this character's figures, in the contract's shape
  const calculationTrace = (value) => {
    const answer = (status, trace, reason = null) => ({
      contractVersion: 1,
      characterId: CHARACTER_ID,
      value,
      status,
      trace,
      content: trace ? { sources: ["Synthetic fixture"], editions: ["2024"], fingerprint: "sha256:00000000000000000000000000000000" } : null,
      reason,
    });
    const step = (operation, v, result, label, source) => ({ operation, value: v, result, label, source, input: null });
    const calculated = (refers) => ({ kind: "calculated", refers });
    const fixed = (name) => ({ kind: "fixed", name });
    const traced = (label, steps) => answer("traced", { key: value, label, final: steps.at(-1).result, steps, excluded: [], notes: [] });
    if (/^(armor-class|hit-points|initiative|passive-perception|speed:|attack:)/.test(value)) return answer("unsupported", null, "The web app works this value out and traces it itself.");
    const [kind, ...rest] = value.split(":");
    const name = rest.join(":");
    const ability = abilities.find((a) => a.name.toLowerCase() === name);
    if (kind === "ability-score" && ability)
      return traced(`${ability.name} score`, [step("start", ability.calculatedScore, ability.calculatedScore, "Base score", { kind: "user-supplied", name: "Base score" })]);
    if (kind === "ability-modifier" && ability) {
      const s = ability.calculatedScore;
      return traced(`${ability.name} modifier`, [
        step("start", s, s, `${ability.name} score`, calculated(`${name}:score`)),
        step("add", -10, s - 10, "Less 10", fixed("Ability modifier")),
        step("divide", 2, (s - 10) / 2, "Half", fixed("Ability modifier")),
        step("round-down", (s - 10) / 2, Math.floor((s - 10) / 2), "Rounded down", fixed("Rounding")),
      ]);
    }
    if (kind === "saving-throw" && ability)
      return traced(`${ability.name} saving throw`, [step("start", ability.calculatedModifier, ability.calculatedModifier, `${ability.name} modifier`, calculated(`${name}:modifier`))]);
    if (value === "proficiency-bonus") return traced("Proficiency bonus", [step("start", 2, 2, "Proficiency Bonus", { kind: "rule", name: "Proficiency Bonus", bonus: "base" })]);
    const skill = ["Arcana", "Investigation", "Perception", "Stealth"].find((s) => s.toLowerCase() === name);
    if (kind === "skill" && skill)
      return traced(skill, [step("start", 3, 3, "Intelligence modifier", calculated("intelligence:modifier")), step("add", 2, 5, "Proficiency", calculated(`${name}:proficiency`))]);
    if (kind === "statistic" && name.endsWith(":proficiency")) return traced(name, [step("start", 2, 2, "Skill Proficiency", { kind: "rule", name: "Wizard", elementType: "Class" })]);
    if (kind === "spell-attack" && name === "wizard")
      return traced("Wizard spell attack", [step("start", 2, 2, "Proficiency bonus", calculated("proficiency")), step("add", 3, 5, "Intelligence modifier", calculated("intelligence:modifier"))]);
    if (kind === "spell-save-dc" && name === "wizard")
      return traced("Wizard spell save DC", [
        step("start", 8, 8, "Base", fixed("Spell save DC")),
        step("add", 2, 10, "Proficiency bonus", calculated("proficiency")),
        step("add", 3, 13, "Intelligence modifier", calculated("intelligence:modifier")),
      ]);
    return answer("missing", null, `The character has no ${name || value}.`);
  };
  return {
    get combat() {
      return combat;
    },
    get magic() {
      return magic;
    },
    choices,
    options,
    entries,
    sources,
    resolve(url, method, body) {
      const path = new URL(url).pathname;
      if (path === "/api/elements/sources")
        return { sources: sources.map((name) => ({ name, group: name.includes("Homebrew") ? "homebrew" : name.includes("Supplement") ? "supplements" : "core", elements: 12 })) };
      if (path === "/api/elements/spell-index") return { version: 1, spells };
      if (path === "/api/elements/items") return { version: 1, items: [] };
      if (path === "/api/elements/compendium/batch") return { entries };
      if (path === "/api/elements/compendium") return { items: entries };
      if (path.startsWith("/api/elements/compendium/"))
        return { ...(entries.find((e) => e.id === path.split("/").at(-1)) ?? entries[2]), supports: [], rules: [] };
      if (path === "/api/campaigns") return { campaigns: [], dm: false };
      if (path === "/api/campaigns/fights") return { fights: [] };
      if (path === "/api/characters/players") return { players: [{ name: "Test Player", characters: 1, locked: false }] };
      if (path === "/api/characters") return method === "POST" ? { id: CHARACTER_ID } : { characters: [character] };
      if (path === "/api/characters/creation-options")
        return { options: [{ id: "test-creation", name: "Test character", shortDescription: "Synthetic fixture" }] };
      if (path === "/api/characters/portrait-options") return { portraits: [] };
      if (path === base) return { character };
      const suffix = path.slice(base.length);
      if (suffix === "/ability-scores") return { abilityScores: abilities };
      if (suffix === "/saving-throws")
        return {
          savingThrows: abilities.map((a) => ({
            name: a.name,
            abilityScoreAbbreviation: a.abbreviation,
            abilityScoreModifier: a.calculatedModifier,
            additionalBonus: 0,
            calculatedBonus: a.calculatedModifier,
          })),
        };
      if (suffix === "/skills")
        return {
          skills: ["Arcana", "Investigation", "Perception", "Stealth"].map((name) => ({
            name,
            abilityScoreAbbreviation: "INT",
            abilityScoreModifier: 3,
            additionalBonus: 2,
            calculatedBonus: 5,
          })),
        };
      if (suffix === "/statistics")
        return {
          statistics: [
            { groupName: "proficiency", totalValue: 2 },
            { groupName: "speed", totalValue: 30 },
            { groupName: "wizard:spellcasting:slots:1", totalValue: 4 },
            { groupName: "wizard:spellcasting:prepare", totalValue: 4 },
          ],
        };
      if (suffix === "/registrations") return { registrations };
      if (suffix === "/classes")
        return { classes: [{ characterClassId: "wizard-class", name: "Wizard", level: 3, isPrimary: true, registrationId: "class-reg" }] };
      if (suffix === "/builder/choices") return { choices, pending: false };
      if (suffix === "/sources") return { restricted: [sources[0]] };
      if (suffix === "/extras") return { extras: [] };
      if (suffix === "/multiclass") return { taken: [], options: [] };
      if (suffix === "/story") return { fields: {} };
      if (suffix === "/inventory") {
        if (method === "PUT") inventory = body;
        return inventory;
      }
      if (suffix === "/spellcasting") return { proficiencyBonus: 2, casters: [caster], otherSpells: [] };
      if (suffix === "/magic") {
        if (method === "PUT") magic = body;
        return magic;
      }
      if (suffix === "/combat") {
        if (method === "PUT") combat = body;
        return combat;
      }
      if (suffix === "/defenses") return { resistances: [], immunities: [], vulnerabilities: [] };
      if (suffix === "/calculation/trace") return calculationTrace(new URL(url).searchParams.get("value") ?? "");
      if (suffix.startsWith("/builder/session")) {
        if (suffix.endsWith("/save")) changed = false;
        return { active: true, since: "2026-01-01T12:00:00Z", changed };
      }
      if (suffix.endsWith("/options")) return { options };
      if (suffix.includes("/selection-rules/")) {
        const choice = choices.find((c) => suffix.includes(c.ruleId));
        if (!choice) throw new Error(`Unknown choice: ${suffix}`);
        const option = options.find((o) => o.elementId === body.elementId);
        choice.selected = suffix.endsWith("/unregister") ? null : { elementId: option.elementId, name: option.name, source: option.source };
        changed = true;
        return {};
      }
      throw new Error(`Unmocked API: ${method} ${path}`);
    },
  };
}
