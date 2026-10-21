/**
 * Imperial or Metric, for display only. The rules, the server, saved characters, Aurora files and the PDF sheet keep
 * feet and pounds; these helpers turn a canonical value into what the reader asked for at the moment it is shown.
 * Metric follows the usual D&D table conversion, not the exact one: 5 ft = 1.5 m (0.3 m per foot, so the 5-foot grid
 * stays in half metres), 1 lb = 0.5 kg, 1 mile = 1.5 km. Feet are converted in tenths (feet × 3 / 10), so whole feet
 * never pick up floating-point noise ("9 m", never "9.144 m").
 */
export type Units = "imperial" | "metric";
export const UNITS = ["imperial", "metric"] as const;
export const UNITS_STORAGE_KEY = "starfall-units";

export function normalizeUnits(value: string | null | undefined): Units {
  return value === "metric" ? "metric" : "imperial";
}

/** a number without trailing zeros: 9, 1.5, 0.25 */
function trim(n: number, decimals: number): string {
  const f = 10 ** decimals;
  return String(Math.round(n * f) / f);
}

/** metres for a distance in feet, to a tenth (the D&D 0.3 m per foot) */
export function feetToMetres(feet: number): number {
  return Math.round(feet * 3) / 10;
}

/** the number part of a distance: "30" (imperial) or "9" (metric) */
export function distanceNumber(feet: number, units: Units): string {
  return units === "metric" ? trim(feetToMetres(feet), 1) : trim(feet, 2);
}

/** the unit after a distance: "ft" or "m" */
export function distanceUnit(units: Units): string {
  return units === "metric" ? "m" : "ft";
}

/** "30 ft" or "9 m" */
export function formatDistance(feet: number, units: Units): string {
  return `${distanceNumber(feet, units)} ${distanceUnit(units)}`;
}

/** "1 mile" or "1.5 km" */
export function formatMiles(miles: number, units: Units): string {
  if (units === "metric") return `${trim(miles * 1.5, 1)} km`;
  return `${trim(miles, 2)} ${miles === 1 ? "mile" : "miles"}`;
}

/** the number part of a weight: "3" (lb) or "1.5" (kg); below 1 kg to the hundredth, so small items stay visible */
export function weightNumber(pounds: number, units: Units): string {
  if (units === "imperial") return trim(pounds, 3);
  const kg = pounds / 2;
  return kg !== 0 && Math.abs(kg) < 1 ? trim(kg, 2) : trim(kg, 1);
}

export function weightUnit(units: Units): string {
  return units === "metric" ? "kg" : "lb";
}

/** "3 lb" or "1.5 kg" */
export function formatWeight(pounds: number, units: Units): string {
  return `${weightNumber(pounds, units)} ${weightUnit(units)}`;
}

/** a weight typed in the reader's unit, back to the pounds that are stored (kg × 2: exact for the 0.5 kg per lb) */
export function poundsFromInput(value: number, units: Units): number {
  return units === "metric" ? value * 2 : value;
}

/** a weight in pounds as the reader's input shows it (the inverse of poundsFromInput) */
export function inputFromPounds(pounds: number, units: Units): number {
  return units === "metric" ? pounds / 2 : pounds;
}

/**
 * A weapon's range or reach as written by the rules data: "5 ft", "10 ft", or a unitless normal/long pair in feet
 * ("80/320", "20/60"). Anything else (a custom attack's own text) is returned unchanged.
 */
export function formatRange(range: string, units: Units): string {
  if (units === "imperial") return range;
  const pair = range.trim().match(/^(\d+(?:\.\d+)?)\s*\/\s*(\d+(?:\.\d+)?)(?:\s*(?:ft\.?|feet))?$/i);
  if (pair) return `${distanceNumber(Number(pair[1]), units)}/${distanceNumber(Number(pair[2]), units)} m`;
  return convertDistanceLabel(range, units);
}

// a distance inside a short label: "60 feet", "60 ft.", "15-foot", "30/120 ft.", "1 mile", "2 miles"
const DISTANCE = /(\d+(?:\.\d+)?)(?:\/(\d+(?:\.\d+)?))?(?:\s|-)(feet|foot|ft\.?)(?![a-z])|(\d+(?:\.\d+)?)\s(miles?)\b/gi;
// labels only: no sentence ends ("." other than "ft."), short; rules prose is never converted
const LABEL = /^[^.;:!?]*(?:ft\.[^.;:!?]*)*$/i;

/**
 * Distances inside a short rules label, such as a spell's range ("60 feet", "Self (15-foot cone)", "1 mile"), a sense
 * ("darkvision 60 ft.") or a reach ("reach 5 ft. or range 20/60 ft."). Longer text and sentences (rules prose,
 * descriptions) are returned unchanged: converting them automatically is not reliable.
 */
export function convertDistanceLabel(text: string, units: Units): string {
  if (units === "imperial" || !text || text.length > 80 || !LABEL.test(text)) return text;
  return text.replace(DISTANCE, (_all, n: string, n2: string | undefined, unit: string | undefined, miles: string | undefined) => {
    if (miles !== undefined) return formatMiles(Number(miles), units);
    const first = distanceNumber(Number(n), units);
    const value = n2 ? `${first}/${distanceNumber(Number(n2), units)}` : first;
    // "15-foot cone" reads "4.5 m cone"; "60 ft." loses its full stop with the unit
    return unit ? `${value} m` : value;
  });
}
