// A Homebrew page edit's preview as the web app reads and shows it, on made-up previews in the contract's shape.
import { ApiError } from "@starlights/api-client";
import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { stalePreviewOf, StaleHomebrewError } from "@/lib/api/homebrew";
import { EditPreview } from "@/pages/homebrew/edit-preview";
import { canSave, expectedRevisionOf, fieldLabel, previewSummary, revisionToSend, type HomebrewEditPreview } from "./homebrew-preview";

const SUBCLASS = "ID_STARLIGHTS_HOMEBREW_SUBCLASS_TEST_CIRCLE";
const ECHO = `${SUBCLASS}_F_TEST_ECHO`;

const edit: HomebrewEditPreview = {
  kind: "subclasses",
  id: SUBCLASS,
  isNew: false,
  revision: "0123456789abcdef",
  changes: [{ field: "description", before: "Old words.", after: "New words." }],
  elements: [
    { elementId: SUBCLASS, name: "Test Circle", change: "changed" },
    { elementId: ECHO, name: "Level 6: Test Echo", change: "removed" },
  ],
  characters: [
    { characterId: "c1", name: "Mira Vale", playerName: "Test Player", uses: [ECHO], loses: [ECHO] },
    { characterId: "c2", name: "Orrin", playerName: null, uses: [SUBCLASS], loses: [] },
  ],
  diagnostics: [],
};

const fresh: HomebrewEditPreview = { kind: "spells", id: "ID_STARLIGHTS_HOMEBREW_SPELL_TEST_SPARKLE", isNew: true, revision: null, changes: [], elements: [{ elementId: "ID_STARLIGHTS_HOMEBREW_SPELL_TEST_SPARKLE", name: "Test Sparkle", change: "added" }], characters: [], diagnostics: [] };

describe("a homebrew edit's preview", () => {
  it("is summed up in a line: what changes, who uses it, who loses something", () => {
    expect(previewSummary(edit)).toBe("2 changes, 1 removed · 2 characters use it, 1 loses something");
    expect(previewSummary(fresh)).toBe("New, nothing uses it yet");
    expect(previewSummary({ ...edit, changes: [], elements: [], characters: [] })).toBe("No changes · no character uses it");
    expect(previewSummary({ ...edit, changes: [], elements: [edit.elements[0]], characters: [] })).toBe("1 change · no character uses it");
  });

  it("names fields as the form does", () => {
    expect(["castingTime", "lists", "asiLevels", "someNewField"].map(fieldLabel)).toEqual(["Casting time", "Class spell lists", "Ability Score Improvement levels", "Some New Field"]);
  });

  it("saves with the revision the form was opened on, the fresh one only after a refusal, and not when refused anyway", () => {
    expect(expectedRevisionOf(edit)).toBe("0123456789abcdef");
    expect(expectedRevisionOf(fresh)).toBeNull();
    // opened on rev-1; a review made after someone saved rev-2 does not change what the save is based on
    expect(revisionToSend("rev-1")).toBe("rev-1");
    expect(revisionToSend("rev-1", null)).toBe("rev-1");
    expect(revisionToSend(undefined)).toBeNull();
    // the save was refused and its fresh review (rev-2, or the record gone) was shown: the next one is based on that
    expect(revisionToSend("rev-1", { ...edit, revision: "rev-2" })).toBe("rev-2");
    expect(revisionToSend("rev-1", { ...edit, isNew: true, revision: null })).toBeNull();
    expect(canSave(edit)).toBe(true);
    expect(canSave({ ...edit, diagnostics: [{ severity: "error", code: "invalid-value", importer: "homebrew", message: "It needs a name.", detail: "name" }] })).toBe(false);
    expect(canSave({ ...edit, diagnostics: [{ severity: "error", code: "stale-revision", importer: "homebrew", message: "Someone saved it." }] })).toBe(true);
    expect(canSave({ ...edit, diagnostics: [{ severity: "warning", code: "replaces-existing", importer: "homebrew", message: "There is already one." }] })).toBe(true);
  });

  it("a refused save's answer (409) is read as a fresh preview", () => {
    const stale = { ...edit, diagnostics: [{ severity: "error", code: "stale-revision", importer: "homebrew", message: "Someone saved it after you opened it." }] };
    const preview = stalePreviewOf(new ApiError(409, "Conflict", JSON.stringify(stale)));
    expect(preview).toEqual(stale);
    expect(new StaleHomebrewError(preview!).message).toBe("Someone saved it after you opened it.");
    expect(stalePreviewOf(new ApiError(400, "Bad Request", JSON.stringify(stale)))).toBeNull();
    expect(stalePreviewOf(new ApiError(409, "Conflict", "not json"))).toBeNull();
    expect(stalePreviewOf(new Error("x"))).toBeNull();
  });

  it("shows the changes, the features removed, and who loses what", () => {
    const html = renderToStaticMarkup(<EditPreview preview={edit} />);
    expect(html).toContain("2 changes, 1 removed");
    expect(html).toContain("Description:</span> Old words.");
    expect(html).toContain("Removed</span>");
    expect(html).toContain("Level 6: Test Echo");
    expect(html).toContain("Mira Vale");
    expect(html).toContain("(Test Player)");
    expect(html).toContain("Loses Level 6: Test Echo at their next update.");
    expect(html).toContain(": Test Circle");
    expect(html).not.toContain(SUBCLASS);
  });
});
