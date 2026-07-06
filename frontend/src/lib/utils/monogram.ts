/**
 * Monospace monogram + tone system from the design language: categories and threads get
 * a 1–2 letter monogram tile tinted either accent-orange or cyan, assigned
 * deterministically so the same category always renders the same tone.
 */

export function monogram(name: string): string {
  const letters = name
    .replace(/&/g, "")
    .split(/\s+/)
    .filter(Boolean)
    .map((word) => word[0])
    .join("")
    .slice(0, 2)
    .toUpperCase();
  return letters || "?";
}

export type MonogramTone = "accent" | "cyan";

export function toneFor(seed: string): MonogramTone {
  let hash = 0;
  for (let i = 0; i < seed.length; i++) {
    hash = (hash * 31 + seed.charCodeAt(i)) | 0;
  }
  return (hash & 1) === 0 ? "accent" : "cyan";
}
