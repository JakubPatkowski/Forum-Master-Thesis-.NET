import { describe, expect, it } from "vitest";

import {
  presenceDotColor,
  presenceLabel,
  presenceMap,
  statusOf,
} from "@/lib/social/presence";

describe("presence derivation (poll-only — never on the realtime bus)", () => {
  it("builds a map from batch entries", () => {
    const map = presenceMap([
      { userId: "01A", status: "online" },
      { userId: "01B", status: "away" },
    ]);
    expect(statusOf(map, "01A")).toBe("online");
    expect(statusOf(map, "01B")).toBe("away");
  });

  it("reads unknown / not-yet-fetched users as offline, never a false online", () => {
    expect(statusOf(presenceMap([]), "01X")).toBe("offline");
    expect(statusOf(presenceMap(undefined), "01X")).toBe("offline");
  });

  it("maps statuses onto the design's dot colors", () => {
    expect(presenceDotColor("online")).toBe("green");
    expect(presenceDotColor("away")).toBe("amber");
    expect(presenceDotColor("offline")).toBe("red");
  });

  it("labels statuses per the design copy", () => {
    expect(presenceLabel("online")).toBe("Active now");
    expect(presenceLabel("away")).toBe("Away");
    expect(presenceLabel("offline")).toBe("Offline");
  });
});
