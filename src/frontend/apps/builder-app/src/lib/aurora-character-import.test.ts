// An Aurora character file's list picks (a background's personality traits, ideals, …) name the item by its number;
// the content import keys each item by its owner and list (AuroraImporter.ListKey), and the two have to agree.
import { describe, expect, it } from "vitest";
import { auroraListItemId } from "./aurora-character-import";

describe("auroraListItemId", () => {
  it("keys a list item the way the content import does", () => {
    expect(auroraListItemId("ID_TEST_BACKGROUND", "Personality Trait", "6")).toBe("list:ID_TEST_BACKGROUND:Personality-Trait:6");
  });

  it("turns every run of other characters into one dash and trims them, like the server's regex", () => {
    expect(auroraListItemId("ID_X", "  Ideal (Lawful) ", "2")).toBe("list:ID_X:Ideal-Lawful:2");
    expect(auroraListItemId("ID_X", "Bond/Flaw", "10")).toBe("list:ID_X:Bond-Flaw:10");
  });
});
