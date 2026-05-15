# Stakeholder Definition — Portfolio (working name)

> Status: exploration draft. The system has no codebase yet; this file
> exists to lock the shape of the problem before any scaffolding.

## Executive Summary

- **What:** A multi-project, multi-team workspace that sits *above* one
  or more headless lifecycle executors and serves as the single front
  door humans use to start, observe, approve, and complete the work
  flowing through them. The portfolio is the **only client** the
  executors expose to end users; everything an end user does — picking
  what to work on, reviewing artefacts, signing off at a checkpoint —
  enters here.
- **Value Proposition:** Organizations get the org-shaped layer
  (projects, teams, members, roles, dashboards, authorization) without
  any lifecycle executor having to model the org. Executors stay
  single-purpose mechanism; the portfolio carries the human and
  organizational context that mechanism alone cannot represent.
- **Success Criteria:**
  1. Multiple projects, each owned by a distinct team with its own
     membership and role assignments, run concurrently against the same
     portfolio instance.
  2. No end-user action requires reaching a lifecycle executor directly.
     Holding executor credentials is an operator-only concern.
  3. A second lifecycle executor (different domain — e.g. incident
     response, data pipeline) can be plugged in by configuration alone,
     without portfolio code changes.
  4. Authorization is end-to-end: a member without the right role on the
     right project cannot advance work, even if they discover the
     downstream API.
  5. Lifecycle-specific review screens (the brief → tasks → plan →
     implementation timeline for feature delivery; future analogues for
     other lifecycles) land as new screens in the portfolio without
     altering any executor.

## Core Business Problem

Lifecycle executors are deliberately headless and single-concern. Each
one drives *one* item through *one* lifecycle and knows nothing about
who owns the work, who is allowed to approve what, which initiative the
work belongs to, or how an organization slices "everything in flight
this week." That context lives today in spreadsheets, chat threads, and
the heads of the people running the system — which is exactly where it
decays under load.

Adding org awareness to each executor would couple every executor to a
specific organizational model, force every new executor to re-implement
the same primitives, and conflate two domains that change for entirely
different reasons (lifecycle mechanics evolve with the *work*; org
structure evolves with the *people*).

**Current Pain Points:**
- Operators must hold out-of-band knowledge to map work items to the
  team or person responsible.
- There is no first-class place to see "all in-flight work for project
  X," let alone aggregate views across projects.
- Authorization is coarse: anyone holding the executor's credentials can
  advance any work item, regardless of role, project, or scope.
- Every new lifecycle (different domain) would re-create its own org
  integration in isolation.
- Stakeholders cannot self-serve. They need an operator to translate
  their question into an executor call.

**Desired Outcome:**
A team member opens the portfolio, picks a project they belong to,
sees every in-flight item in it, opens one to read the current artefact,
and acts on a checkpoint waiting on their role — all without knowing or
caring which lifecycle executor handles the work behind the scenes. A
second project on a second lifecycle works the same way, in the same
application, with the same identity. Operators stop being the routing
layer.

## Architectural Position

```
┌──────────────────────┐               ┌────────────────────┐
│ End users            │   actions     │                    │   commands     ┌──────────────────┐
│  · members           │──────────────▶│      Portfolio     │──────────────▶│ Lifecycle exec A │
│  · reviewers         │               │                    │                │ (e.g. feature    │
│  · stakeholders      │◀──────────────│  facade · UI ·     │◀──────────────│  lifecycle)      │
│                      │   live state  │  org context ·     │   live state   └──────────────────┘
└──────────────────────┘               │  authorization     │
                                       │                    │   commands     ┌──────────────────┐
┌──────────────────────┐   admin       │  owner of:         │──────────────▶│ Lifecycle exec B │
│ Operators            │──────────────▶│   projects · teams │                │ (future, e.g.    │
│ (configure execs,    │               │   members · roles  │◀──────────────│  incident flow)  │
│  hold credentials)   │◀──────────────│   assignments      │                └──────────────────┘
└──────────────────────┘               └────────────────────┘
```

Four hard rules follow:

1. **Lifecycle executors are headless backends.** End users never reach
   them directly. The portfolio is the only client that holds executor
   credentials. If a future consumer ever needs lifecycle state, it
   consumes it through the portfolio.
2. **The portfolio is the single front door for humans.** Every
   end-user action — start work, review an artefact, approve a
   checkpoint, watch progress — enters here, gets authorized here, and
   is forwarded from here.
3. **Org context lives only in the portfolio.** Projects, teams,
   members, roles, and assignments are portfolio entities. No lifecycle
   executor learns about them. Authorization happens at the portfolio
   boundary, before any forward to an executor.
4. **The portfolio is a transparent facade for live state.** Streamed
   traces and frequently-polled details must pass through without
   buffering, batching, or transformation that adds perceptible
   latency. Adding intelligence in the middle of a stream is forbidden.

A rule of thumb: if a feature would require an end user to obtain
executor credentials, or would require an executor to learn about a
project or team, it is outside the architecture.

## Product Philosophy

1. **Org context is the portfolio; mechanism is the executor.**
   Anything that depends on *who may do what* or *what belongs where*
   lives here. Anything that is "advance state machine X by step Y"
   lives downstream. When in doubt, the test is: would a different
   organization need to model this differently? If yes, it's portfolio.
2. **Single front door, single identity.** End users sign in once, see
   every project they belong to, and act in any of them with the same
   credentials. The portfolio multiplexes downstream so that the user
   never assembles credentials per-executor.
3. **Executor-agnostic by construction.** A second lifecycle executor
   joins the system through configuration: register its address,
   declare which project types route to it, list its checkpoint
   contracts. No portfolio code change for the routine case.
4. **Lifecycle-aware screens, executor-agnostic API.** The portfolio
   may render highly opinionated lifecycle-specific views (timelines,
   diff panels, decision histories), but it does so on top of the
   generic primitives the executor exposes. New lifecycle screens land
   in the portfolio; new endpoints land in the executor; the two evolve
   independently.
5. **Authorization is end-to-end and pessimistic.** Every action
   resolves to (member, role, project, target) and is denied by default.
   A member cannot act on a project they do not belong to, regardless
   of how they reached the action.
6. **Transparent live state is non-negotiable.** Live trace streams and
   status polling must feel native — adding the portfolio in the middle
   cannot introduce perceptible lag or hide events. Consumers will
   forgive the portfolio for being a facade; they will not forgive it
   for being a bottleneck.
7. **Projects own everything.** Every work item, every run, every
   approval, every audit entry is scoped to exactly one project. There
   is no global pool of work and no implicit cross-project state. Cases
   that look cross-project are modeled as inter-project handoffs, not
   shared scope.
8. **The portfolio is opinionated about people; agnostic about
   problems.** It has strong opinions on how a workspace, a team, and a
   role behave. It has no opinion on what a "good plan" or "valid
   implementation" looks like — that is the executor's domain.

## Scope Lock

### In Scope (v1)

- **Workspace primitives.** Projects, teams, members, role assignments,
  and project memberships, all with first-class CRUD.
- **Identity and end-to-end authorization.** Every action resolves to a
  member identity and a permission check on the targeted project before
  any downstream call leaves the portfolio.
- **Lifecycle executor registry.** Operators can declare one or more
  lifecycle executors and bind project types to them. Adding a second
  registered executor of a known shape requires configuration only.
- **Generic facade surface.** Portfolio-mediated entry points to start
  work, send checkpoint signals, fetch state, and stream live progress
  — all scoped to the requesting member's permissions.
- **Generic UI** for browsing projects and work items, managing teams
  and members, and inspecting any in-flight run at the level the
  underlying executor exposes.
- **At least one lifecycle-aware screen.** Demonstrating the rendering
  pattern against the existing feature-delivery lifecycle, end to end,
  including all checkpoint approvals.
- **Operator dashboard.** Cross-project view of in-flight work and
  pending approvals — the routing-layer replacement.
- **Audit trail.** Every portfolio-mediated action recorded with member
  identity, project, target, and outcome.
- **Notification surface for pending action.** A member must be able to
  discover that a checkpoint is waiting on their role without polling
  every project manually. (Mechanism unspecified at this layer; the
  requirement is the *signal*, not the channel.)

### Explicitly Out of Scope

- **Direct executor access for end users.** No "power-user" path that
  bypasses the portfolio. Operators may use executor-native admin tools
  for debugging; end users may not.
- **Authoring lifecycle agents inside the portfolio.** Creating or
  editing the underlying lifecycle definitions is the executor's
  concern, not the portfolio's.
- **Cross-project work items or shared lifecycle state.** Each work
  item belongs to exactly one project. Coordination across projects is
  modeled as separate items linked at the portfolio layer, never as
  shared downstream state.
- **Capacity planning, time tracking, resource allocation.** The
  portfolio knows who *may* act, not who *should* act or for *how
  long*.
- **General-purpose communications platform.** Discussion threads,
  meeting scheduling, document collaboration — all out. The portfolio
  surfaces work and approvals; broader collaboration is somebody
  else's product.
- **Hosted multi-tenant SaaS.** v1 assumes one portfolio instance per
  organization. Multi-tenant hosting is a later, deliberate evolution.
- **Reporting and analytics beyond the operator dashboard.** Counts of
  in-flight work and pending approvals, yes. Velocity charts, burndown,
  forecast, no.
- **Modifications to lifecycle executors to make portfolio integration
  easier.** Composition over extension applies in the other direction
  too: if the portfolio needs something an executor does not expose,
  the request lands as a feature in that executor's own backlog, not
  as a private back-channel.

## Success Metrics

| Metric | Target | How Measured |
|--------|--------|--------------|
| Concurrent projects | ≥3 distinct projects, each owned by a distinct team, running concurrently | Live count from the workspace registry |
| Authorization correctness | 100% of unauthorized end-user actions denied at the portfolio boundary | Audit log: every denied action shows the failed permission check, never reaches the executor |
| Facade transparency | The portfolio adds negligible latency over a direct executor call on streamed and polled paths (P95 within an order of magnitude of the executor's own latency) | Synthetic comparison: same call direct-vs-portfolio, P95 measured |
| Executor independence | A second registered lifecycle executor of a known shape comes online with zero portfolio code changes | Configuration diff only; no code merge required |
| Front-door discipline | Zero end-user actions bypass the portfolio in production | Audit: every action that mutates lifecycle state has a portfolio-issued correlation marker |
| Operator self-service ratio | ≥80% of checkpoints are acted on by the responsible member without operator intervention | Audit: ratio of checkpoint signals issued by the responsible member vs. by an operator on their behalf |

## User Flow Summary

1. **Entry:** A member signs into the portfolio with their identity and
   sees the projects they belong to.
2. **Onboarding:** First action is browsing in-flight work in a project,
   or starting a new work item from a brief that the member is
   authorized to create.
3. **Core Action:** The member acts on a checkpoint waiting for their
   role — reviews an artefact, approves a step, sends back a rejection.
   The portfolio resolves the action against the member's role on the
   project, then forwards to the right lifecycle executor under the
   hood.
4. **Value Moment:** The member sees the work advance in real time,
   never aware of which executor handled the transition or what
   credentials it required. A teammate on a different project does the
   same thing in the same application against a possibly-different
   executor.
5. **Return Trigger:** A pending-action notification (channel
   unspecified) brings the member back when a checkpoint waiting on
   their role appears in any project they belong to.

## AI Task Generation Notes

- **Honor the front-door rule.** Any new feature reachable by end users
  must enter through the portfolio. Direct-executor paths are operator
  tools, not v1 product surface, and never the answer to a user-facing
  problem.
- **Org primitives belong here, not downstream.** Projects, teams,
  members, roles, and assignments are portfolio entities. Do not
  propose adding any of them to a lifecycle executor.
- **Authorization happens before any forward.** Every new portfolio
  entry point that wraps an executor call must declare the (role,
  project, target) check it performs and verify it before the forward.
- **Streaming is hot path.** Any new portfolio surface that wraps an
  executor stream must pass through, not buffer, batch, or transform.
  Adding intelligence in the middle of a stream is a review blocker.
- **Lifecycle-specific UI lives in the portfolio.** A request for a new
  per-agent screen lands in the portfolio, not as a separate
  application and not in the executor.
- **Honor executor independence.** If a feature seems to require a
  change to a downstream executor, surface that as a request against
  the executor's backlog. Do not introduce private back-channels or
  portfolio-only executor variants to dodge it.
- **Reference this document** for cross-cutting concerns
  (notifications, audit, dashboards) — most belong here or in a
  downstream executor, almost never in both.

## Open Questions (deliberately unresolved)

- Working name. "Portfolio" is provisional and finance-flavored;
  alternatives like "Workspace" or "Hub" remain on the table.
- Whether the v1 portfolio supports a single registered executor with a
  config path to add a second, or natively supports N from day one.
  (Both satisfy the success criterion; the work shape differs.)
- Notification channel(s). The requirement is "the member discovers
  pending action without polling"; the channel is an implementation
  choice deferred to the design phase.
- Identity provider posture. v1 needs an identity surface; whether the
  portfolio owns it or federates is a design-phase decision.
- The exact shape of the executor registry contract — what an executor
  must declare to be pluggable. Will fall out of the second-executor
  exercise.

## Changelog

- **2026-05-14** — Initial exploration draft. Captures the layer above
  the lifecycle executors as the home for projects, teams, members,
  roles, authorization, and the user-facing UI (including
  lifecycle-aware screens). Locks the four architectural rules
  (executors are headless; portfolio is the single front door; org
  context is portfolio-only; portfolio is a transparent facade for
  live state). Working name: portfolio.
