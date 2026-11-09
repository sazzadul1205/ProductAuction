# Status String → Enum Refactoring Checklist

## 1. Scope Identification

- **Inventory status fields**: List every entity/model property representing status (including nullable/result variants) and its current allowed values/defaults.
- **Search for literal usage**: Find all string literals, constants, and interpolations tied to each status across the solution (controllers, services, background jobs, DTOs, validators, mappers, repositories, queries, seeders).
- **Database touchpoints**: Identify tables/columns, constraints/defaults, indexes/filtered indexes, views, stored procedures/functions/triggers, and any raw SQL that references status values.
- **API surface**: Catalog request/response DTOs, query/path parameters, filters, sorting/paging logic, and documented contracts (OpenAPI/Swagger, README, client docs) that expose status.
- **UI/client impact**: Locate all pages/components consuming the status (badges, filters/tabs, forms, conditional rendering, i18n labels, color/icon mappings, analytics events).
- **Shared artifacts**: Check shared constants, enums-in-waiting, config files, test fixtures/factories, seed data, and import/export scripts.
- **Ownership map**: Assign an owner/reviewer for each affected area to ensure nothing is missed.

## 2. Data Migration Strategy

- **Baseline assessment**: Snapshot distinct existing values per status column (including nulls, casing/whitespace variants, unexpected legacy values).
- **Mapping plan**: Define an explicit old-value → new-enum-name mapping; decide handling for unknown/invalid values (normalize, quarantine, or fail migration).
- **Compatibility decision**: Choose storage/contract approach (e.g., persist enum names vs numeric) and ensure it preserves meaning of existing records.
- **Migration design**: Plan schema/data steps (add/alter column or conversion, backfill, enforce constraints/defaults) with ordering that avoids data loss.
- **Idempotency & safety**: Ensure migrations are repeatable, transactional where supported, and include pre/post verification queries.
- **Rollback plan**: Define how to revert schema/data and how long dual-read/old-values support remains available.
- **Environment sequencing**: Validate on a production-like copy first; schedule backups and a maintenance window if required.

## 3. Code Impact Analysis

- **Domain/model layer**: Update property types/defaults; confirm nullability and any computed/derived status logic.
- **Persistence/ORM mapping**: Verify conversion configuration, query translation, default values, and concurrency behavior; review LINQ/raw SQL comparisons.
- **Business logic**: Audit conditionals, switches, state transitions, guards, and background jobs for completeness (including “impossible” states and fallbacks).
- **Validation & input binding**: Update request validation, model binding/parsing, case sensitivity, and error messages for invalid statuses.
- **API contracts**: Confirm serialization/deserialization format, backward compatibility, filtering/sorting semantics, and OpenAPI schema accuracy.
- **Authorization/workflows**: Check role/state-based permissions, notifications, audit logging, and any status-driven integrations/webhooks.
- **Cross-boundary consumers**: Review frontend, mobile, reports, exports, and external services for assumptions about string values.
- **Dead/unused values**: Decide per the audit whether to exclude, deprecate, or retain unused statuses; document rationale.

## 4. Testing Requirements

- **Unit tests**: Cover enum mapping, defaults, state transitions, and all conditional branches (including edge/invalid values).
- **Integration tests**: Verify database read/write, migrations/backfill results, and API request/response behavior end-to-end.
- **Contract tests**: Validate serialization format and OpenAPI/client expectations; test backward/forward compatibility if clients update independently.
- **UI tests**: Exercise pages/components for rendering, filters/tabs, forms, badges, and status-dependent actions across roles.
- **Data verification**: Run queries to confirm no unexpected values remain; spot-check records with prior edge cases.
- **Regression & non-functional**: Re-test related workflows (payments/auctions/disputes, etc.), performance of status-filtered queries, and error handling.
- **Rollout verification**: Prepare smoke tests and monitoring checks (logs/metrics/alerts) to run immediately after deployment.

## 5. Deployment Considerations

- **Release strategy**: Prefer backward-compatible rollout (support old and new representations during transition) or a coordinated deploy if breaking.
- **Sequencing**: Define order for migration, backend, and client releases; gate features if needed to avoid mixed-version failures.
- **Configuration & feature flags**: Use flags/toggles for risky behavior changes; ensure defaults are safe.
- **Operational readiness**: Confirm backup/restore, migration runtime, lock impact, and on-call/runbook coverage.
- **Communication**: Notify stakeholders of contract changes, timelines, and any required client updates; update documentation/changelog.
- **Post-deploy monitoring**: Watch error rates, validation failures, and data anomalies; execute smoke tests and be ready to roll back.
- **Cleanup plan**: Schedule removal of temporary compatibility code, deprecated values, and obsolete constants once stable.
