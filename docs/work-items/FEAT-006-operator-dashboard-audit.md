# Feature Brief: FEAT-006 — Operator Dashboard & Audit Log

## 1. Identity

| Field | Value |
|-------|-------|
| **ID** | FEAT-006 |
| **Name** | Operator Dashboard & Audit Log |
| **Target Version** | v1 |
| **Status** | Not Started |
| **Priority** | High |
| **Requested By** | Stakeholder ("Operator dashboard"; "Audit trail") |
| **Date Created** | 2026-05-15 |

## 2. User Story

**As an** operator, **I want to** see in-flight work and pending approvals across every project in one place, plus inspect the audit log of every portfolio-mediated action, **so that** I stop being the routing layer and can debug failed forwards quickly.

## 3. Goal

Cross-project dashboard + project- and workspace-scoped audit log. Validates Success Metrics "Authorization correctness 100%" (every denial is in the log) and "Front-door discipline" (every Granted action carries a portfolio-issued correlation marker).

## 4. Feature Scope

### 4.1 Included

- Audit module: AuditEntry append-only writes from every mutation in every module; query endpoints.
- Endpoints: `GET /api/projects/{id}/audit`, `GET /api/admin/audit`.
- Operator dashboard UI: in-flight totals, pending approvals (aggregated), recent denies, recent executor failures.
- Audit log UI: filterable table; expandable `details_json`.

### 4.2 Excluded

- Velocity charts, burndown, forecasting (explicitly out of stakeholder scope).
- Bulk export of audit (later improvement; not blocking v1).

## 5. Acceptance Criteria

- **AC-1:** Every mutation across every module produces exactly one `AuditEntry` row inside the same transaction as the change.
- **AC-2:** Every authorization denial across every façade endpoint produces exactly one `AuditEntry` row with outcome `Denied` and a reason string.
- **AC-3:** Operator dashboard surfaces (a) total in-flight WorkItems, (b) pending approvals grouped by project, (c) last 50 audit entries with outcome ∈ {Denied, Failed}.
- **AC-4:** Project audit log is visible to any project member (Project:any); cross-project audit is operator-only.
- **AC-5:** No UPDATE or DELETE statement executes against `audit.audit_entries` in any test scenario.

## 6. Key Entities and Business Rules

| Entity | Role | Rules |
|--------|------|-------|
| AuditEntry | Append-only record | INSERT only; never updated, never deleted |

## 7. API Impact

`GET /api/projects/{id}/audit`, `GET /api/admin/audit`. Plus: every existing endpoint writes an audit row (this is a cross-cutting requirement, not a new endpoint).

## 8. UI Impact

| Screen | Status | Description |
|--------|--------|-------------|
| Operator dashboard | New | Cross-project view |
| Audit log (project + admin) | New | Filterable table |

## 9. Edge Cases

- Audit write fails inside a mutation transaction → the whole mutation rolls back. (Audit is never optional.)
- Audit row count grows large — pagination + filters keep queries fast (indexes on `(project_id, occurred_at desc)`, `(acting_member_id, occurred_at desc)`, `(outcome, occurred_at desc)`).
- A project is soft-deleted but its audit entries remain — audit queries always return them (project_id reference is preserved).

## 10. Constraints

- Audit must be in the same transaction as the mutation; never best-effort, never deferred.
- No UPDATE/DELETE on audit_entries from application code.

## 11. Motivation and Priority Justification

**Motivation:** Without dashboard + audit, operators have nothing better than chat threads; without audit, authorization correctness is unverifiable.
**Impact if delayed:** Cannot evidence Success Metrics "Authorization correctness", "Front-door discipline", or "Operator self-service ratio."
**Dependencies on this feature:** None block.

## 12. Traceability

| Reference | Link |
|-----------|------|
| **Persona** | (Operator + project members for project-scoped audit) |
| **Stakeholder Scope Item** | "Operator dashboard"; "Audit trail" |
| **Success Metric** | "Authorization correctness 100%"; "Front-door discipline"; "Operator self-service ratio" |
| **Related Work Items** | Blocked by FEAT-001, FEAT-002, FEAT-004. |
