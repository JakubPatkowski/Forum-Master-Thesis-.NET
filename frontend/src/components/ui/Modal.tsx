"use client";

import { useEffect, type ReactNode } from "react";
import { createPortal } from "react-dom";

import { CornerBrackets } from "@/components/ui/CornerBrackets";

import styles from "./Modal.module.css";

export interface ModalProps {
  open: boolean;
  onClose: () => void;
  title: string;
  /** Small mono line under the title (e.g. the endpoint note from the design). */
  subtitle?: string;
  headerExtra?: ReactNode;
  footer?: ReactNode;
  children: ReactNode;
  width?: number;
}

export function Modal({
  open,
  onClose,
  title,
  subtitle,
  headerExtra,
  footer,
  children,
  width = 780,
}: ModalProps) {
  useEffect(() => {
    if (!open) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };
    document.addEventListener("keydown", onKey);
    document.body.style.overflow = "hidden";
    return () => {
      document.removeEventListener("keydown", onKey);
      document.body.style.overflow = "";
    };
  }, [open, onClose]);

  if (!open) return null;

  return createPortal(
    <>
      <div className={styles.scrim} onClick={onClose} />
      <div className={styles.layer} role="dialog" aria-modal="true" aria-label={title}>
        <div className={styles.modal} style={{ width: `min(${width}px, 100%)` }}>
          <CornerBrackets />
          <div className={styles.header}>
            <div className={styles.headerText}>
              <h1 className={styles.title}>{title}</h1>
              {subtitle ? <div className={styles.subtitle}>{subtitle}</div> : null}
            </div>
            {headerExtra}
            <button className={styles.close} onClick={onClose} title="Close" aria-label="Close">
              <svg
                width="15"
                height="15"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2.5"
              >
                <path d="M18 6 6 18M6 6l12 12" />
              </svg>
            </button>
          </div>
          <div className={`${styles.body} panel-scroll`}>{children}</div>
          {footer ? <div className={styles.footer}>{footer}</div> : null}
        </div>
      </div>
    </>,
    document.body,
  );
}
