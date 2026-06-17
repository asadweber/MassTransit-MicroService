---
name: project-microservice-overview
description: Architecture and stack of the microService repo (MassTransit saga order system)
metadata:
  type: project
---

Repo is a MassTransit/RabbitMQ saga-based order-processing system, net10.0, MassTransit 9.2.0-develop.150. Projects: WebApp (publish-only API + dashboard), OrderSaga (owns OrderStateMachine), InventoryService/PaymentService/NotificationService (each owns one consumer + queue), Contracts (shared messages/saga/consumers), Db.Repository (AppDbContext, EF migrations).

Key non-obvious pattern: every service calls `AddAllConsumers()` registering ALL consumers+saga in every process so the WebApp MassTransit dashboard sees full topology; only the owning service passes `ownerConsumerType`/`configureSagaRepository` to actually create a queue/repo, others get `ExcludeFromConfigureEndpoints()`.

Full details live in repo root `CLAUDE.md` — authoritative source, this is just a pointer/summary.

**Why:** documented via /init on 2026-06-17 so future sessions don't re-derive architecture from scratch.
**How to apply:** read CLAUDE.md first for repo work; use this to recall gist before deciding whether to re-read it.
