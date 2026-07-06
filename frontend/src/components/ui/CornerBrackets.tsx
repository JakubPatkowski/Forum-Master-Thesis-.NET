import styles from "./CornerBrackets.module.css";

/**
 * The cyan corner-bracket greeble from the design language. Parent must be
 * position:relative. `corners="two"` marks only the top-left/bottom-right pair
 * (used on avatars); "four" frames the whole card (auth card, modal, banners).
 */
export function CornerBrackets({
  corners = "four",
  size = 12,
}: {
  corners?: "two" | "four";
  size?: number;
}) {
  const style = { width: size, height: size };
  return (
    <>
      <span className={`${styles.corner} ${styles.tl}`} style={style} aria-hidden />
      {corners === "four" ? (
        <span className={`${styles.corner} ${styles.tr}`} style={style} aria-hidden />
      ) : null}
      {corners === "four" ? (
        <span className={`${styles.corner} ${styles.bl}`} style={style} aria-hidden />
      ) : null}
      <span className={`${styles.corner} ${styles.br}`} style={style} aria-hidden />
    </>
  );
}
