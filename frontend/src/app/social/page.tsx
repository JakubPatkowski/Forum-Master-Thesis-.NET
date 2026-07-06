"use client";

/**
 * Social / Community — UI-ONLY PREVIEW (design: Social.dc.html).
 *
 * The backend has NO Social module (optional phase, skipped) and the Files module
 * rejects dm uploads with 422. Everything on this page is local in-memory mock state:
 * no fetch calls, no WebSocket subscriptions. The persistent PREVIEW banner makes that
 * unmistakable (scope decision from the build brief).
 */

import Link from "next/link";
import { useState } from "react";

import { PageShell } from "@/components/layout/PageShell";
import { Badge } from "@/components/ui/Badge";
import { LiveDot } from "@/components/ui/LiveDot";

import styles from "./social.module.css";

interface MockFriend {
  id: string;
  initial: string;
  name: string;
  username: string;
  status: "online" | "away" | "offline";
  lastSeen?: string;
}

interface MockMessage {
  id: number;
  me: boolean;
  text: string;
  time: string;
}

const FRIENDS: MockFriend[] = [
  { id: "f1", initial: "T", name: "Tomasz Labiak", username: "tomek_labs", status: "online" },
  { id: "f2", initial: "O", name: "Aleksandra Nowak", username: "dev_ola", status: "online" },
  { id: "f3", initial: "P", name: "Piotr Gmerek", username: "piotr_gm", status: "away" },
  {
    id: "f4",
    initial: "A",
    name: "Anna Lenska",
    username: "ana_lens",
    status: "offline",
    lastSeen: "2 h",
  },
  {
    id: "f5",
    initial: "M",
    name: "Marek Kowal",
    username: "marek_soldering",
    status: "offline",
    lastSeen: "1 d",
  },
];

const INITIAL_REQUESTS = [
  { id: "r1", initial: "S", name: "Stella Obrycka", username: "stella_obs", mutual: 4 },
  { id: "r2", initial: "K", name: "Kuba Rybak", username: "kuba_fish", mutual: 1 },
];

const GROUPS = [
  { id: "g1", initial: "HL", name: "Home Lab Crew", members: 5, online: 3 },
  { id: "g2", initial: "AI", name: "Astro Imagers", members: 12, online: 4 },
  { id: "g3", initial: "NW", name: ".NET Wrocław", members: 28, online: 9 },
];

const INITIAL_CHAT: MockMessage[] = [
  { id: 1, me: false, text: "Did you disable ABC on the MH-Z19 in the end?", time: "24m" },
  {
    id: 2,
    me: true,
    text: "Yeah — sending the 0x79 command on boot killed the drift completely.",
    time: "20m",
  },
  { id: 3, me: false, text: "Nice. Mind sharing the systemd unit?", time: "12m" },
];

function statusColor(status: MockFriend["status"]) {
  return status === "online" ? "green" : status === "away" ? "amber" : "red";
}

export default function SocialPage() {
  const [tab, setTab] = useState<"friends" | "requests" | "groups" | "ignored">("friends");
  const [requests, setRequests] = useState(INITIAL_REQUESTS);
  const [chatWith, setChatWith] = useState<MockFriend | null>(FRIENDS[0] ?? null);
  const [messages, setMessages] = useState<MockMessage[]>(INITIAL_CHAT);
  const [draft, setDraft] = useState("");

  const online = FRIENDS.filter((f) => f.status !== "offline");
  const offline = FRIENDS.filter((f) => f.status === "offline");

  const send = () => {
    const text = draft.trim();
    if (!text) return;
    setMessages((list) => [...list, { id: Date.now(), me: true, text, time: "now" }]);
    setDraft("");
  };

  return (
    <PageShell>
      <div className={styles.previewBanner} role="note">
        <Badge tone="warning">PREVIEW</Badge>
        <span>
          Social is a design preview — the backend has no Social module. Everything here is local
          mock state: no data is sent or stored.{" "}
          <Link href="/" className={styles.previewLink}>
            Back to the real forum →
          </Link>
        </span>
      </div>

      <div className={styles.grid}>
        <section className={styles.rail}>
          <div className={styles.railHeader}>
            <LiveDot color="green" size={8} />
            <span className={styles.railTitle}>SOCIAL</span>
            <span className={styles.railCount}>{online.length} online</span>
          </div>

          <div className={styles.tabs}>
            {(
              [
                ["friends", "Friends"],
                ["requests", "Requests"],
                ["groups", "Groups"],
                ["ignored", "Ignored"],
              ] as const
            ).map(([key, label]) => (
              <button
                key={key}
                className={tab === key ? styles.tabActive : styles.tab}
                onClick={() => setTab(key)}
              >
                {label}
                {key === "requests" && requests.length > 0 ? (
                  <span className={styles.tabBadge}>{requests.length}</span>
                ) : null}
              </button>
            ))}
          </div>

          <div className={`${styles.railBody} panel-scroll`}>
            {tab === "friends" ? (
              <>
                <div className={styles.groupLabel}>ONLINE · {online.length}</div>
                {online.map((friend) => (
                  <div key={friend.id} className={styles.friendRow}>
                    <span className={styles.friendAvatar}>
                      {friend.initial}
                      <span
                        className={`${styles.statusDot} ${styles[statusColor(friend.status)]}`}
                      />
                    </span>
                    <span className={styles.friendText}>
                      <span className={styles.friendName}>{friend.name}</span>
                      <span className={styles.friendStatus}>
                        {friend.status === "online" ? "Active now" : "Away"}
                      </span>
                    </span>
                    <button
                      className={styles.messageIcon}
                      title="Open chat (mock)"
                      onClick={() => setChatWith(friend)}
                    >
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor">
                        <path d="M4 4h16a1 1 0 0 1 1 1v11a1 1 0 0 1-1 1H8l-5 4V5a1 1 0 0 1 1-1z" />
                      </svg>
                    </button>
                  </div>
                ))}
                <div className={styles.groupLabel}>OFFLINE · {offline.length}</div>
                {offline.map((friend) => (
                  <div key={friend.id} className={`${styles.friendRow} ${styles.friendOffline}`}>
                    <span className={styles.friendAvatar}>
                      {friend.initial}
                      <span className={`${styles.statusDot} ${styles.red}`} />
                    </span>
                    <span className={styles.friendText}>
                      <span className={styles.friendName}>{friend.name}</span>
                      <span className={styles.friendStatus}>last seen {friend.lastSeen}</span>
                    </span>
                  </div>
                ))}
              </>
            ) : null}

            {tab === "requests" ? (
              <>
                <div className={styles.groupLabel}>INCOMING</div>
                {requests.length === 0 ? (
                  <div className={styles.railEmpty}>No pending requests.</div>
                ) : (
                  requests.map((request) => (
                    <div key={request.id} className={styles.requestCard}>
                      <div className={styles.requestHead}>
                        <span className={styles.requestAvatar}>{request.initial}</span>
                        <span className={styles.friendText}>
                          <span className={styles.friendName}>{request.name}</span>
                          <span className={styles.friendStatus}>
                            @{request.username} · {request.mutual} mutual
                          </span>
                        </span>
                      </div>
                      <div className={styles.requestActions}>
                        <button
                          className={styles.accept}
                          onClick={() => setRequests((r) => r.filter((x) => x.id !== request.id))}
                        >
                          ACCEPT
                        </button>
                        <button
                          className={styles.decline}
                          onClick={() => setRequests((r) => r.filter((x) => x.id !== request.id))}
                        >
                          DECLINE
                        </button>
                      </div>
                    </div>
                  ))
                )}
              </>
            ) : null}

            {tab === "groups"
              ? GROUPS.map((group) => (
                  <div key={group.id} className={styles.friendRow}>
                    <span className={styles.requestAvatar}>{group.initial}</span>
                    <span className={styles.friendText}>
                      <span className={styles.friendName}>{group.name}</span>
                      <span className={styles.friendStatus}>
                        {group.members} members · {group.online} online
                      </span>
                    </span>
                  </div>
                ))
              : null}

            {tab === "ignored" ? (
              <div className={styles.railEmpty}>
                Ignored users can&apos;t message you or see your online status. You haven&apos;t
                ignored anyone.
              </div>
            ) : null}
          </div>
        </section>

        <section className={styles.chat}>
          {chatWith ? (
            <>
              <div className={styles.chatHeader}>
                <span className={styles.friendAvatar}>
                  {chatWith.initial}
                  <span className={`${styles.statusDot} ${styles[statusColor(chatWith.status)]}`} />
                </span>
                <span className={styles.friendText}>
                  <span className={styles.friendName}>{chatWith.name}</span>
                  <span className={styles.friendStatus}>@{chatWith.username}</span>
                </span>
                <Badge tone="warning">MOCK</Badge>
              </div>
              <div className={`${styles.chatBody} panel-scroll`}>
                <span className={styles.chatNote}>
                  LOCAL PREVIEW · MESSAGES ARE NOT SENT ANYWHERE
                </span>
                {messages.map((message) => (
                  <div
                    key={message.id}
                    className={message.me ? styles.bubbleRowMe : styles.bubbleRow}
                  >
                    <div className={styles.bubbleCol}>
                      <span className={message.me ? styles.bubbleMe : styles.bubble}>
                        {message.text}
                      </span>
                      <span className={styles.bubbleTime}>{message.time}</span>
                    </div>
                  </div>
                ))}
              </div>
              <div className={styles.chatComposer}>
                <input
                  className={styles.chatInput}
                  value={draft}
                  onChange={(e) => setDraft(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") {
                      e.preventDefault();
                      send();
                    }
                  }}
                  placeholder="Message… (mock — stays in this tab)"
                />
                <button className={styles.sendButton} onClick={send} title="Send (mock)">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor">
                    <path d="M3 20.5 21 12 3 3.5 3 10l12 2-12 2z" />
                  </svg>
                </button>
              </div>
            </>
          ) : (
            <div className={styles.railEmpty}>Pick a friend to open a chat preview.</div>
          )}
        </section>
      </div>
    </PageShell>
  );
}
