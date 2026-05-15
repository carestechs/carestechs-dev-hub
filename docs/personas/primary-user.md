# Persona: The Project Member Acting on a Checkpoint

> A one-line archetype: "The reviewer who needs to advance work on their project without ever touching the executor's plumbing."

## Who They Are

A member of one or more project teams in an organization that runs work through one or more headless lifecycle executors. They are the human a checkpoint waits on — to read an artefact, to approve, to send back. They are not the operator who stood up the executors; they are not a power user of any single executor. They live in DevHub.

- **Role/Title:** Project member — practically a reviewer, approver, or stakeholder on the work that flows through a lifecycle executor (feature brief reviewer, plan approver, implementation sign-off, future analogues for other lifecycles).
- **Key Characteristics:** Belongs to one or more teams; assigned to one or more projects; has a role on each project that decides which checkpoints they may act on. Comfortable with structured review work; not interested in plumbing.
- **Relationship with Technology:** Web app user. Expects single sign-on, real-time updates, and that "the right thing for me to do next" is visible without searching. Will not type API calls, will not hold executor credentials, and will not learn which executor handled which work item.

## Core Problem

A member needs to act on the work waiting on their role, across every project they belong to, without becoming a router themselves.

- **The Problem:** Today, finding "what is waiting on me" requires asking an operator, scanning chat threads, or polling per-executor tools the member has no business holding credentials for.
- **Current Workaround:** Spreadsheets and chat pings from an operator who has mapped work items to people by hand.
- **Why That Fails:** It decays under load, it depends on one operator's attention, and it leaks authorization: anyone with the executor credentials can advance anything, regardless of role.
- **Consequences of Inaction:** Checkpoints stall; operators become a permanent routing layer; second lifecycles can't be added because the org integration would have to be redone from scratch.

## Why This Persona First

- **Pain Acuity:** Highest at the moment a checkpoint is waiting — the work cannot advance until the right human acts, and today nothing surfaces that signal cleanly.
- **Market Size:** Every project in scope has multiple members; every checkpoint targets a role, not an individual operator. DevHub's whole value lands on this persona.
- **Willingness to Pay/Adopt:** This is the persona Success Criterion #2 is written for ("no end-user action requires reaching a lifecycle executor directly"). Adoption is non-optional in scope.
- **Strategic Fit:** Serving this persona is what makes a second lifecycle executor possible to plug in without rewriting the org layer — see Success Criterion #3.

## Other Segments Considered

| Segment | Why Not First |
|---------|---------------|
| Operators (configure executors, hold credentials) | Important, but already served by executor-native admin tools and the operator dashboard. Not where v1 product surface lives. |
| Executor authors / lifecycle designers | Out of scope: "authoring lifecycle agents inside DevHub" is explicitly excluded. |
| Cross-org stakeholders / multi-tenant viewers | Out of scope: v1 is one DevHub instance per organization. |

## AI Task Generation Notes

> These notes help AI assistants generate better tasks for this persona.

- **User Context:** Authenticated member with a known identity and a set of (project, role) tuples. Every screen and every action must resolve through that tuple set.
- **Peak Pain Moment:** A checkpoint is waiting on their role and they don't know it yet. Tasks that close this gap (notifications, "waiting on you" surfaces, lifecycle-aware review screens) are first-class.
- **Success Looks Like:** The member opens DevHub, sees what's waiting on them across all their projects, acts on it, and watches the work advance in real time — without learning which executor handled it.
- **Anti-Patterns:** Do not propose anything that requires the member to hold executor credentials, learn executor URLs, or pick "which executor" before acting. Do not propose cross-project shared state. Do not buffer or batch live streams in DevHub.
