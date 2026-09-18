import { describe, expect, it } from "vitest";
import { collectIds } from "./org-chart";
import type { OrgChartNode } from "@/services/employees.service";

/**
 * H-05: the org chart used to open the first two levels by default (root + department heads + their whole
 * team), which pulled in the widest possible subtree on first render and pushed the root off-center. These
 * tests lock down the depth semantics `collectIds` relies on so a regression back to depth 2 fails loudly.
 */
function node(id: string, children: OrgChartNode[] = []): OrgChartNode {
  return { id, displayName: id, orgUnitId: null, children };
}

describe("collectIds", () => {
  const tree: OrgChartNode[] = [
    node("root", [
      node("dept-head-a", [node("member-a1"), node("member-a2")]),
      node("dept-head-b", [node("member-b1")]),
    ]),
  ];

  it("with maxDepth 1, expands only the root (not department heads' teams)", () => {
    const expanded = collectIds(tree, new Set(), 1);
    expect(expanded).toEqual(new Set(["root"]));
  });

  it("with maxDepth 2 (the old default), also expands department heads", () => {
    const expanded = collectIds(tree, new Set(), 2);
    expect(expanded).toEqual(new Set(["root", "dept-head-a", "dept-head-b"]));
  });

  it("with no maxDepth, expands every node that has children", () => {
    const expanded = collectIds(tree, new Set());
    expect(expanded).toEqual(new Set(["root", "dept-head-a", "dept-head-b"]));
  });

  it("returns an empty set for a chart with no nodes", () => {
    expect(collectIds([], new Set(), 1)).toEqual(new Set());
  });

  it("does not add leaf nodes (no children) regardless of depth", () => {
    const leafOnly: OrgChartNode[] = [node("solo")];
    expect(collectIds(leafOnly, new Set(), 5)).toEqual(new Set());
  });
});
