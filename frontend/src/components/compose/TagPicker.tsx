"use client";

/**
 * Tag chips + autocomplete dropdown (design: Compose.dc.html). Client-side validation
 * mirrors the backend regex exactly: lowercase-kebab-case, ≤32 chars, max 5 tags.
 * Suggestions are mocked until the backend grows a tag-search endpoint (see
 * use-tag-suggestions.ts); free-text entry always works and is the real get-or-create.
 */

import { useState, type KeyboardEvent } from "react";

import { Badge } from "@/components/ui/Badge";
import { TagChip } from "@/components/ui/TagChip";
import { useTagSuggestions } from "@/lib/hooks/use-tag-suggestions";

import styles from "./TagPicker.module.css";

export const TAG_SLUG_RE = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;
export const MAX_TAGS = 5;
export const MAX_TAG_LENGTH = 32;

export function validateTagSlug(slug: string): string | null {
  if (!TAG_SLUG_RE.test(slug) || slug.length > MAX_TAG_LENGTH) {
    return "Tags must be lowercase-kebab-case, max 32 chars.";
  }
  return null;
}

export function TagPicker({
  tags,
  onChange,
  disabled = false,
}: {
  tags: string[];
  onChange: (tags: string[]) => void;
  disabled?: boolean;
}) {
  const [input, setInput] = useState("");
  const [open, setOpen] = useState(false);
  const [highlighted, setHighlighted] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const suggestions = useTagSuggestions(input, tags);

  const addTag = (raw: string) => {
    const slug = raw.trim();
    if (!slug) return;
    const problem = validateTagSlug(slug);
    if (problem) {
      setError(problem);
      return;
    }
    if (!tags.includes(slug) && tags.length < MAX_TAGS) {
      onChange([...tags, slug]);
    }
    setInput("");
    setError(null);
    setOpen(false);
    setHighlighted(0);
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "Enter") {
      event.preventDefault();
      const pick = open ? suggestions[highlighted] : undefined;
      addTag(pick ? pick.slug : input);
    } else if (event.key === "Escape") {
      setOpen(false);
    } else if (event.key === "ArrowDown" && suggestions.length > 0) {
      event.preventDefault();
      setOpen(true);
      setHighlighted((h) => (h + 1) % suggestions.length);
    } else if (event.key === "ArrowUp" && suggestions.length > 0) {
      event.preventDefault();
      setHighlighted((h) => (h - 1 + suggestions.length) % suggestions.length);
    } else if (event.key === "Backspace" && input === "" && tags.length > 0) {
      onChange(tags.slice(0, -1));
    }
  };

  return (
    <div className={styles.field}>
      <label className={styles.label}>
        Tags{" "}
        <span className={styles.count}>
          · {tags.length}/{MAX_TAGS}
        </span>
      </label>
      <div className={styles.anchor}>
        <div className={styles.box}>
          {tags.map((slug) => (
            <TagChip
              key={slug}
              slug={slug}
              onRemove={disabled ? undefined : () => onChange(tags.filter((t) => t !== slug))}
            />
          ))}
          {!disabled && tags.length < MAX_TAGS ? (
            <input
              className={styles.input}
              value={input}
              placeholder={tags.length ? "add another…" : "e.g. home-lab"}
              onChange={(e) => {
                setInput(e.target.value);
                setOpen(true);
                setHighlighted(0);
                setError(null);
              }}
              onKeyDown={onKeyDown}
              onFocus={() => setOpen(true)}
              onBlur={() => setTimeout(() => setOpen(false), 120)}
              aria-label="Add tag"
            />
          ) : null}
        </div>
        {open && suggestions.length > 0 && tags.length < MAX_TAGS ? (
          <div className={styles.dropdown}>
            <div className={styles.dropdownHeader}>
              <span className={styles.dropdownLabel}>EXISTING TAGS</span>
              <Badge
                tone="warning"
                title="Requires GET /api/content/tags?query= — proposed endpoint, mock data"
              >
                SOON
              </Badge>
            </div>
            {suggestions.map((suggestion, index) => (
              <button
                key={suggestion.slug}
                type="button"
                className={index === highlighted ? styles.optionActive : styles.option}
                onMouseDown={(e) => {
                  e.preventDefault();
                  addTag(suggestion.slug);
                }}
                onMouseEnter={() => setHighlighted(index)}
              >
                #{suggestion.slug}
              </button>
            ))}
          </div>
        ) : null}
      </div>
      {error ? <span className={styles.error}>{error} (422 · Validation)</span> : null}
    </div>
  );
}
