# ADR 0007 — Argon2id for password hashing

**Status:** Accepted

**Context.** Passwords must be stored with a memory-hard, side-channel-resistant KDF. PBKDF2 (what B/gomx uses)
is acceptable but memory-cheap and easier to attack on GPUs/ASICs; OWASP's first recommendation for new systems
is **Argon2id**. The two thesis apps must share the same password-hashing property so auth security is not a
confounding variable.

**Decision.** Hash passwords with **Argon2id** via a maintained .NET binding (e.g. `Konscious.Security.Cryptography.Argon2`),
storing the standard PHC-encoded string (`$argon2id$v=19$m=…,t=…,p=…$salt$hash`) in `users.password_hash`.
Baseline parameters (tunable per OWASP, benchmarked on the target node): **m = 19 MiB (or higher), t = 2,
p = 1**, 16-byte random salt, 32-byte output. Verification is constant-time; a login miss still spends
comparable time (dummy verify) so timing never reveals whether an account exists. The encoded parameters live in
the hash, so cost can be raised later and rehash-on-login applied.

**Consequences.** (+) Strong, modern, GPU-resistant hashing; parameters self-describing and upgradable.
(−) Higher CPU/RAM per login than PBKDF2 — relevant to the auth-throughput benchmark, so login cost is reported
as a measured figure. The shared contract requires B to move from PBKDF2 to Argon2id (or both adopt the same KDF)
so auth is comparable; see `../../../docs/architecture/PROPOZYCJA-UJEDNOLICENIA-A-B.md` §5.
