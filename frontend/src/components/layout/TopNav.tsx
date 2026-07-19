"use client";

/**
 * Primary navigation (design: TopNav.dc.html): FORUM://SIGNAL logo, global search,
 * the WS status pill (LIVE / RECONNECTING / OFFLINE), the friends+messages entry
 * points with their real unread badges (see SocialNavLinks), a notification bell
 * backed by the realtime activity log, and the user menu. The bracketed tab row
 * (01 FORUM · 02 SEARCH · 03 PROFILE) sits underneath. No Admin tab — the admin UI
 * is a later increment (scope decision).
 */

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useRef, useState, type FormEvent } from "react";

import { useCompose } from "@/components/compose/compose-context";
import { Avatar } from "@/components/ui/Avatar";
import { Button } from "@/components/ui/Button";
import { LiveDot } from "@/components/ui/LiveDot";
import { useAuth } from "@/lib/auth/auth-context";
import { useConversations, useUnreadNotificationCount } from "@/lib/hooks/use-social";
import { notificationHref } from "@/lib/realtime/notification-href";
import { useRealtime } from "@/lib/realtime/realtime-context";
import { totalUnreadMessages } from "@/lib/social/conversations";
import { timeAgoLabel } from "@/lib/utils/time";

import styles from "./TopNav.module.css";

function useClickOutside(onOutside: () => void) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const handler = (event: MouseEvent) => {
      if (ref.current && !ref.current.contains(event.target as Node)) onOutside();
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, [onOutside]);
  return ref;
}

function WsStatusPill() {
  const { status } = useRealtime();
  const { isAuthenticated } = useAuth();
  if (!isAuthenticated) return null;

  if (status === "live") {
    return (
      <span className={styles.wsPill} title="WebSocket connected">
        <LiveDot color="cyan" size={7} />
        <span className={styles.wsLive}>LIVE</span>
      </span>
    );
  }
  if (status === "connecting" || status === "reconnecting") {
    return (
      <span className={styles.wsPill} title="WebSocket disconnected — trying to reconnect">
        <LiveDot color="amber" size={7} />
        <span className={styles.wsReconnecting}>RECONNECTING</span>
      </span>
    );
  }
  return (
    <span className={styles.wsPill} title="WebSocket offline">
      <LiveDot color="red" pulse={false} size={7} />
      <span className={styles.wsOffline}>OFFLINE</span>
    </span>
  );
}

function ActivityBell() {
  const { activity } = useRealtime();
  const [open, setOpen] = useState(false);
  const ref = useClickOutside(() => setOpen(false));
  const [seenCount, setSeenCount] = useState(0);
  const hasUnseen = activity.length > seenCount;

  return (
    <div className={styles.menuAnchor} ref={ref}>
      <button
        className={styles.iconButton}
        title="Realtime activity"
        onClick={() => {
          setOpen((v) => !v);
          setSeenCount(activity.length);
        }}
      >
        <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor">
          <path d="M12 22a2 2 0 0 0 2-2h-4a2 2 0 0 0 2 2zm6-6v-5a6 6 0 1 0-12 0v5l-2 2v1h16v-1l-2-2z" />
        </svg>
        {hasUnseen ? <span className={styles.bellDot} /> : null}
      </button>
      {open ? (
        <div className={styles.dropdown}>
          <div className={styles.dropdownHeader}>
            <LiveDot color="cyan" size={7} />
            <span>LIVE ACTIVITY</span>
          </div>
          {activity.length === 0 ? (
            <div className={styles.dropdownEmpty}>Nothing yet — events show up as they happen.</div>
          ) : (
            activity.slice(0, 8).map((entry, index) => {
              const href = notificationHref(entry.notification);
              const content = (
                <>
                  <span
                    className={
                      entry.notification.entity === "reaction"
                        ? styles.activitySquareAccent
                        : styles.activitySquareCyan
                    }
                  />
                  <span className={styles.activityText}>
                    {entry.notification.entity}.{entry.notification.type}
                  </span>
                  <span className={styles.activityTime}>
                    {timeAgoLabel(new Date(entry.receivedAt).toISOString())}
                  </span>
                </>
              );
              const key = `${entry.receivedAt}-${index}`;
              return href ? (
                <Link
                  href={href}
                  className={`${styles.activityRow} ${styles.activityRowLink}`}
                  key={key}
                  onClick={() => setOpen(false)}
                >
                  {content}
                </Link>
              ) : (
                <div className={styles.activityRow} key={key}>
                  {content}
                </div>
              );
            })
          )}
          <div className={styles.dropdownFooter}>WS NOTIFY → RE-FETCH · NO PAYLOADS</div>
        </div>
      ) : null}
    </div>
  );
}

/**
 * The two social entry points carry two INDEPENDENT badges: the friends/bell badge is
 * the durable-notification unread count (friend/group activity — message arrivals
 * never create notification rows), the messages badge is the client-side sum of
 * per-conversation unread counts. Both are push-invalidated via the own-user view.
 */
function SocialNavLinks() {
  const unreadNotifications = useUnreadNotificationCount();
  const conversations = useConversations();
  const notificationCount = unreadNotifications.data?.unread ?? 0;
  const messageCount = totalUnreadMessages(conversations.data ?? []);

  return (
    <>
      <Link href="/social" className={styles.iconButton} title="Friends & notifications">
        <svg width="17" height="17" viewBox="0 0 24 24" fill="currentColor">
          <path d="M16 11a4 4 0 1 0-4-4 4 4 0 0 0 4 4zm-8 0a4 4 0 1 0-4-4 4 4 0 0 0 4 4zm0 2c-2.7 0-8 1.34-8 4v2h9v-2c0-1.6.77-2.9 1.96-3.79A11.9 11.9 0 0 0 8 13zm8 0c-.35 0-.72.02-1.1.06C16.09 14.06 17 15.36 17 17v2h7v-2c0-2.66-5.3-4-8-4z" />
        </svg>
        {notificationCount > 0 ? (
          <span className={styles.badgeCyan}>
            {notificationCount > 99 ? "99+" : notificationCount}
          </span>
        ) : null}
      </Link>
      <Link href="/social" className={styles.iconButton} title="Messages">
        <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor">
          <path d="M4 4h16a1 1 0 0 1 1 1v11a1 1 0 0 1-1 1H8l-5 4V5a1 1 0 0 1 1-1z" />
        </svg>
        {messageCount > 0 ? (
          <span className={styles.badgeAccent}>{messageCount > 99 ? "99+" : messageCount}</span>
        ) : null}
      </Link>
    </>
  );
}

function UserMenu() {
  const { currentUser, logout, logoutAll } = useAuth();
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const ref = useClickOutside(() => setOpen(false));

  if (!currentUser) return null;

  return (
    <div className={styles.menuAnchor} ref={ref}>
      <button className={styles.userButton} onClick={() => setOpen((v) => !v)}>
        <Avatar userId={currentUser.id} displayName={currentUser.username} size={28} />
        <span className={styles.userName}>{currentUser.username}</span>
        <svg
          width="12"
          height="12"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2.5"
        >
          <path d="m6 9 6 6 6-6" />
        </svg>
      </button>
      {open ? (
        <div className={styles.dropdown}>
          <div className={styles.dropdownUser}>
            <div className={styles.dropdownUserName}>{currentUser.username}</div>
            <div className={styles.dropdownUserMeta}>
              @{currentUser.username} · {currentUser.roles.join(", ") || "user"}
            </div>
          </div>
          <Link
            className={styles.dropdownItem}
            href={`/u/${currentUser.id}`}
            onClick={() => setOpen(false)}
          >
            Your profile
          </Link>
          <Link className={styles.dropdownItem} href="/settings" onClick={() => setOpen(false)}>
            Account settings
          </Link>
          <div className={styles.dropdownDivider} />
          <button
            className={styles.dropdownItem}
            onClick={() => {
              setOpen(false);
              void logout().then(() => router.push("/"));
            }}
          >
            Log out
          </button>
          <button
            className={`${styles.dropdownItem} ${styles.dropdownDanger}`}
            onClick={() => {
              setOpen(false);
              void logoutAll().then(() => router.push("/"));
            }}
          >
            Log out all devices
          </button>
        </div>
      ) : null}
    </div>
  );
}

export function TopNav() {
  const pathname = usePathname();
  const router = useRouter();
  const { isAuthenticated, currentUser } = useAuth();
  const { openCreate } = useCompose();
  const [query, setQuery] = useState("");

  const onSearch = (event: FormEvent) => {
    event.preventDefault();
    const q = query.trim();
    if (q) router.push(`/search?q=${encodeURIComponent(q)}`);
  };

  const isForum = pathname === "/" || pathname.startsWith("/c/") || pathname.startsWith("/t/");
  const isSearch = pathname.startsWith("/search");
  const isProfile = pathname.startsWith("/u/");

  return (
    <header className={styles.header}>
      <div className={styles.topRow}>
        <Link href="/" className={styles.logo}>
          <span className={styles.logoMark}>
            <span className={styles.logoDot} />
          </span>
          <span className={styles.logoText}>
            FORUM<span className={styles.logoSlashes}>:{"//"}</span>SIGNAL
          </span>
        </Link>

        <form className={styles.search} onSubmit={onSearch} role="search">
          <svg
            className={styles.searchIcon}
            width="15"
            height="15"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2.5"
          >
            <circle cx="11" cy="11" r="7" />
            <path d="M20.5 20.5 16 16" />
          </svg>
          <input
            className={styles.searchInput}
            placeholder="Search threads, users, tags…"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            aria-label="Search"
          />
        </form>

        <div className={styles.right}>
          <WsStatusPill />
          {isAuthenticated && currentUser ? (
            <>
              <Button size="sm" onClick={() => openCreate()}>
                + New thread
              </Button>
              <SocialNavLinks />
              <ActivityBell />
              <UserMenu />
            </>
          ) : (
            <div className={styles.anonActions}>
              <Link href="/auth" className={styles.loginLink}>
                Log in
              </Link>
              <Link href="/auth?tab=register" className={styles.registerLink}>
                Register
              </Link>
            </div>
          )}
        </div>
      </div>

      {/* prefetch={false}: these persistent tabs re-render on every realtime status/activity tick;
          with default prefetch each re-render can re-arm the viewport prefetch and flood the network
          with /<route>?_rsc=… round-trips. The routes are client-rendered (React Query) anyway, so the
          RSC shell prefetch buys almost nothing here. */}
      <nav className={styles.tabs}>
        <div className={styles.tabsInner}>
          <Link href="/" prefetch={false} className={isForum ? styles.tabActive : styles.tab}>
            <span className={isForum ? styles.tabNumActive : styles.tabNum}>01</span> FORUM
          </Link>
          <Link href="/search" prefetch={false} className={isSearch ? styles.tabActive : styles.tab}>
            <span className={isSearch ? styles.tabNumActive : styles.tabNum}>02</span> SEARCH
          </Link>
          <Link
            href={currentUser ? `/u/${currentUser.id}` : "/auth"}
            prefetch={false}
            className={isProfile ? styles.tabActive : styles.tab}
          >
            <span className={isProfile ? styles.tabNumActive : styles.tabNum}>03</span> PROFILE
          </Link>
        </div>
      </nav>
    </header>
  );
}
