import { describe, expect, it } from "vitest";
import { convertDistanceLabel, formatDistance, formatMiles, formatRange, formatWeight, inputFromPounds, normalizeUnits, poundsFromInput } from "./units";

describe("units", () => {
  it("keeps Imperial as the default and for anything unknown", () => {
    expect(normalizeUnits(null)).toBe("imperial");
    expect(normalizeUnits("")).toBe("imperial");
    expect(normalizeUnits("SI")).toBe("imperial");
    expect(normalizeUnits("metric")).toBe("metric");
  });

  it("converts distances the D&D way: 5 ft = 1.5 m, no stray precision", () => {
    const table: [number, string][] = [[5, "1.5 m"], [10, "3 m"], [15, "4.5 m"], [20, "6 m"], [30, "9 m"], [60, "18 m"], [90, "27 m"], [120, "36 m"], [25, "7.5 m"], [35, "10.5 m"], [0, "0 m"], [300, "90 m"]];
    for (const [feet, metric] of table) {
      expect(formatDistance(feet, "metric")).toBe(metric);
      expect(formatDistance(feet, "imperial")).toBe(`${feet} ft`);
    }
    // every multiple of 5 ft up to a mile is a whole or half metre
    for (let feet = 0; feet <= 5280; feet += 5) expect(formatDistance(feet, "metric")).toMatch(/^\d+(\.5)? m$/);
  });

  it("converts miles and pounds with the table factors", () => {
    expect(formatMiles(1, "metric")).toBe("1.5 km");
    expect(formatMiles(1, "imperial")).toBe("1 mile");
    expect(formatMiles(2, "imperial")).toBe("2 miles");
    expect(formatWeight(1, "metric")).toBe("0.5 kg");
    expect(formatWeight(10, "metric")).toBe("5 kg");
    expect(formatWeight(3, "metric")).toBe("1.5 kg");
    expect(formatWeight(0.25, "metric")).toBe("0.13 kg");
    expect(formatWeight(0.05, "metric")).toBe("0.03 kg");
    expect(formatWeight(165, "metric")).toBe("82.5 kg");
    expect(formatWeight(2.5, "imperial")).toBe("2.5 lb");
  });

  it("round-trips a typed weight to the stored pounds exactly", () => {
    for (const lb of [0, 0.5, 1, 2.5, 3, 10, 18, 65, 150]) {
      expect(poundsFromInput(inputFromPounds(lb, "metric"), "metric")).toBe(lb);
      expect(poundsFromInput(inputFromPounds(lb, "imperial"), "imperial")).toBe(lb);
    }
    expect(poundsFromInput(1.5, "metric")).toBe(3);
  });

  it("converts weapon ranges and reach", () => {
    expect(formatRange("5 ft", "metric")).toBe("1.5 m");
    expect(formatRange("10 ft", "metric")).toBe("3 m");
    expect(formatRange("80/320", "metric")).toBe("24/96 m");
    expect(formatRange("20/60", "metric")).toBe("6/18 m");
    expect(formatRange("80/320", "imperial")).toBe("80/320");
    expect(formatRange("Self", "metric")).toBe("Self");
    expect(formatRange("whatever the player wrote. Really.", "metric")).toBe("whatever the player wrote. Really.");
  });

  it("converts distances inside short rules labels only", () => {
    expect(convertDistanceLabel("60 feet", "metric")).toBe("18 m");
    expect(convertDistanceLabel("Self (15-foot cone)", "metric")).toBe("Self (4.5 m cone)");
    expect(convertDistanceLabel("Self (30-foot radius)", "metric")).toBe("Self (9 m radius)");
    expect(convertDistanceLabel("1 mile", "metric")).toBe("1.5 km");
    expect(convertDistanceLabel("Touch", "metric")).toBe("Touch");
    expect(convertDistanceLabel("Sight", "metric")).toBe("Sight");
    expect(convertDistanceLabel("darkvision 60 ft.", "metric")).toBe("darkvision 18 m");
    expect(convertDistanceLabel("blindsight 10 ft. (blind beyond this radius)", "metric")).toBe("blindsight 3 m (blind beyond this radius)");
    expect(convertDistanceLabel("reach 5 ft. or range 20/60 ft.", "metric")).toBe("reach 1.5 m or range 6/18 m");
    expect(convertDistanceLabel("30 ft., fly 60 ft. (hover)", "metric")).toBe("9 m, fly 18 m (hover)");
    // sentences and long rules text stay as written
    const prose = "Each creature in a 20-foot-radius sphere must make a save. It takes 8d6 fire damage.";
    expect(convertDistanceLabel(prose, "metric")).toBe(prose);
    expect(convertDistanceLabel("60 feet", "imperial")).toBe("60 feet");
  });
});
