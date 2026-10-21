import { createContext, useContext, useEffect, useMemo, useState } from "react";
import {
  convertDistanceLabel,
  distanceNumber,
  distanceUnit,
  formatDistance,
  formatRange,
  formatWeight,
  inputFromPounds,
  normalizeUnits,
  poundsFromInput,
  UNITS_STORAGE_KEY,
  weightNumber,
  weightUnit,
  type Units,
} from "@/lib/units";

/** The reader's measurement units with formatters bound to them (lib/units.ts); display only, saved per browser. */
type UnitsContext = {
  units: Units;
  setUnits: (units: Units) => void;
  /** "30 ft" / "9 m" */
  distance: (feet: number) => string;
  distanceNumber: (feet: number) => string;
  distanceUnit: string;
  /** "3 lb" / "1.5 kg" */
  weight: (pounds: number) => string;
  weightNumber: (pounds: number) => string;
  weightUnit: string;
  /** a weapon's range or reach ("5 ft", "80/320") */
  range: (range: string) => string;
  /** distances inside a short rules label (a spell's range, a sense); prose is left as written */
  label: (text: string) => string;
  /** a weight typed in the reader's unit to stored pounds, and back */
  poundsFromInput: (value: number) => number;
  inputFromPounds: (pounds: number) => number;
};

const Context = createContext<UnitsContext | null>(null);

function read(): Units {
  try {
    return normalizeUnits(localStorage.getItem(UNITS_STORAGE_KEY));
  } catch {
    return "imperial";
  }
}

export function UnitsProvider({ children }: { children: React.ReactNode }) {
  const [units, update] = useState<Units>(read);
  // another tab changed it
  useEffect(() => {
    const changed = (event: StorageEvent) => {
      if (event.key === UNITS_STORAGE_KEY) update(normalizeUnits(event.newValue));
    };
    window.addEventListener("storage", changed);
    return () => window.removeEventListener("storage", changed);
  }, []);
  const value = useMemo<UnitsContext>(
    () => ({
      units,
      setUnits(next) {
        update(next);
        try {
          localStorage.setItem(UNITS_STORAGE_KEY, next);
        } catch {
          /* still applies for this visit when storage is unavailable */
        }
      },
      distance: (feet) => formatDistance(feet, units),
      distanceNumber: (feet) => distanceNumber(feet, units),
      distanceUnit: distanceUnit(units),
      weight: (pounds) => formatWeight(pounds, units),
      weightNumber: (pounds) => weightNumber(pounds, units),
      weightUnit: weightUnit(units),
      range: (range) => formatRange(range, units),
      label: (text) => convertDistanceLabel(text, units),
      poundsFromInput: (value) => poundsFromInput(value, units),
      inputFromPounds: (pounds) => inputFromPounds(pounds, units),
    }),
    [units],
  );
  return <Context.Provider value={value}>{children}</Context.Provider>;
}

// a component outside the provider (tests, the printable sheet) reads Imperial, the canonical units
const IMPERIAL: UnitsContext = {
  units: "imperial",
  setUnits: () => {},
  distance: (feet) => formatDistance(feet, "imperial"),
  distanceNumber: (feet) => distanceNumber(feet, "imperial"),
  distanceUnit: "ft",
  weight: (pounds) => formatWeight(pounds, "imperial"),
  weightNumber: (pounds) => weightNumber(pounds, "imperial"),
  weightUnit: "lb",
  range: (range) => range,
  label: (text) => text,
  poundsFromInput: (value) => value,
  inputFromPounds: (pounds) => pounds,
};

// eslint-disable-next-line react-refresh/only-export-components -- the provider's hook, kept beside it like useTheme
export function useUnits() {
  return useContext(Context) ?? IMPERIAL;
}
