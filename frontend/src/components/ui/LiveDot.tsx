import styles from "./LiveDot.module.css";

/** The pulsing status dot — cyan (live), amber (reconnecting), red (offline), green (online). */
export function LiveDot({
  color = "cyan",
  pulse = true,
  size = 7,
}: {
  color?: "cyan" | "amber" | "red" | "green" | "accent";
  pulse?: boolean;
  size?: number;
}) {
  return (
    <span
      className={[styles.dot, styles[color], pulse ? styles.pulse : undefined]
        .filter(Boolean)
        .join(" ")}
      style={{ width: size, height: size }}
      aria-hidden
    />
  );
}
