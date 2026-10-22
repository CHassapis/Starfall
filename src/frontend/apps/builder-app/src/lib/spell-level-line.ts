/** A spell's level line as the sheet writes it: "Evocation Cantrip", "3rd-level evocation (ritual)". No app state, so the PDF worker can use it. */
const ORDINAL = ["Cantrip", "1st", "2nd", "3rd", "4th", "5th", "6th", "7th", "8th", "9th"];

export const spellLevelLine = (s: { level: number; school: string; ritual?: boolean }) =>
  s.level === 0 ? `${s.school} Cantrip` : `${ORDINAL[s.level]}-level ${s.school.toLowerCase()}${s.ritual ? " (ritual)" : ""}`;
