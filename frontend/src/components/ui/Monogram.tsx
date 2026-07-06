import styles from "./Monogram.module.css";

import { monogram, toneFor, type MonogramTone } from "@/lib/utils/monogram";

export interface MonogramProps {
  name: string;
  /** Explicit tone; defaults to a deterministic tone from the seed (name). */
  tone?: MonogramTone | "neutral";
  seed?: string;
  size?: number;
  active?: boolean;
  className?: string;
}

/** Square mono-typeface tile — the design's stand-in wherever an icon/avatar slot is empty. */
export function Monogram({
  name,
  tone,
  seed,
  size = 30,
  active = false,
  className,
}: MonogramProps) {
  const resolvedTone = tone ?? toneFor(seed ?? name);
  return (
    <span
      className={[styles.tile, styles[resolvedTone], active ? styles.active : undefined, className]
        .filter(Boolean)
        .join(" ")}
      style={{ width: size, height: size, fontSize: Math.max(10, Math.round(size * 0.36)) }}
      aria-hidden
    >
      {monogram(name)}
    </span>
  );
}
