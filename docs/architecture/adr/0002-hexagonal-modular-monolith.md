# ADR 0002 — Module-first modular monolith (hexagonal inside modules)

**Status:** Accepted (supersedes the initial layer-first scaffold)

**Context.** Master's thesis benchmarks a decoupled React+.NET stack (Architecture A) against a Go SSR
monolith (Architecture B). The .NET app must be professional and enterprise-grade, yet idiomatic — an
over-engineered structure would unfairly inflate build-time / developer-velocity metrics. The first scaffold
was **layer-first** (`Core.Domain/Modules/Identity`, `Core.Application/Modules/Identity`, …): many projects,
each module smeared across them.

**Decision.** Reorganize **module-first**. The solution is grouped by business module, not technical layer:

```
src/Bootstrap/Forum.Api            single executable; discovers IModule implementations
src/Shared/{SharedKernel,Common,Infrastructure}
src/Modules/{Identity,Content,Files,Engagement}/Forum.Modules.X
        Domain/ Application/ Infrastructure/ Presentation/ Contracts/  + XModule.cs
```

Each module is **one project** with hexagonal folders inside. Everything is `internal` except the module's
`Contracts/` surface; modules interact only via Contracts + integration events. ~13 projects in three folders.

**Why module-first beats layer-first here.** In a modular monolith the boundary that matters most is *between
modules* — and module-as-project makes a cross-module internal reference a **compile error** (other modules
see only public `Contracts`). In-module layer purity (Domain not using Infrastructure) is the lesser concern
and is enforced by `NetArchTest` instead of the compiler. We deliberately trade compiler-enforced *layers*
(what layer-first gives) for compiler-enforced *module isolation* (what a modular monolith actually needs),
plus higher cohesion (a feature lives in one folder) and clean future extraction of a module to a service.

**Consequences.** Fewer, cohesive projects; the top level "screams" the domain. Per-module DbContext + schema +
migrations. Cross-cutting layer rules rely on architecture tests (must stay green in CI). `Forum.Api` keeps an
explicit module list (no reflection magic) for readability and testability.
