import { describe, expect, it } from "vitest";
import { spellcastingFigures } from "./sheet-figures";

describe("a spellcasting's attack bonus and save DC on the sheet", () => {
  it("are the server's, item bonuses included", () => {
    // they were proficiency + modifier (2 + Cha 3: +5, DC 13) on the sheet, in the battle view and in the sheet
    // texts' placeholders, while the server, the PDF's spell pages and the magic tab had +6 and DC 14 with a rod's +1
    expect(spellcastingFigures("Ref Bard", 3, 2, [{ name: "Ref Bard", attack: 6, dc: 14 }])).toEqual({ attackBonus: 6, saveDc: 14 });
    expect(spellcastingFigures("ref bard", 3, 2, [{ name: "Ref Bard", attack: 6, dc: 14 }])).toEqual({ attackBonus: 6, saveDc: 14 });
  });

  it("are proficiency + modifier for a spellcasting the server has not worked out", () => {
    expect(spellcastingFigures("Ref Wizard", 3, 2, [{ name: "Ref Bard", attack: 6, dc: 14 }])).toEqual({ attackBonus: 5, saveDc: 13 });
    expect(spellcastingFigures("Ref Wizard", 3, 2, undefined)).toEqual({ attackBonus: 5, saveDc: 13 });
  });
});
