import { useState } from "react";
import { Input } from "@/components/ui/input";
import { useUnits } from "@/components/units-provider";

/**
 * A weight typed in the reader's units (lb or kg) and stored in pounds. The stored value changes only when the field is
 * edited, so opening a form in Metric and saving it untouched keeps the exact pounds. While typing, the text stays as
 * typed ("1." keeps its point); it shows the stored value again when the field loses focus.
 */
export function WeightInput({ pounds, onPounds, min = 0, ...props }: Omit<React.ComponentProps<typeof Input>, "value" | "onChange" | "min"> & {
  pounds: number | null | undefined;
  onPounds: (pounds: number | null) => void;
  min?: number;
}) {
  const u = useUnits();
  const [draft, setDraft] = useState<string | null>(null);
  const shown = pounds === null || pounds === undefined ? "" : String(Math.round(u.inputFromPounds(pounds) * 1000) / 1000);
  return (
    <Input
      inputMode="decimal"
      {...props}
      value={draft ?? shown}
      onChange={(e) => {
        const text = e.target.value.replace(/[^\d.]/g, "");
        setDraft(text);
        onPounds(text === "" ? null : Math.max(min, u.poundsFromInput(Number(text) || 0)));
      }}
      onBlur={(e) => {
        setDraft(null);
        props.onBlur?.(e);
      }}
    />
  );
}
