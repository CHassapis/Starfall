import { toast } from "sonner";
import { describeRoll, type Roll } from "@/lib/lore/dice";

/** A roll as the app shows one: "Damage: 9", with "2d6 + 3 → 4 + 2 + 3 = 9" under it. */
export function showRoll(title: string, expression: string, r: Roll) {
  toast(`${title}: ${r.total}`, { description: `${expression.replace(/\s+/g, " ")} → ${describeRoll(r)}` });
}
