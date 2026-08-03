# WorkflowDemo — dynamic approval workflows with a visual designer

A .NET 8 solution where **non-technical users define approval rules through a web UI**, see the
resulting flow as an auto-generated diagram, and run requests through those rules. Different
requesters can follow different chains — e.g. a team member needs N+1 *and* N+2 approval, while a
manager or IT director needs only one.

## How it works

1. **Templates are data, not code.** A workflow template (JSON, stored in SQLite) holds routing
   rules. Each rule = a condition on the requester (e.g. `role in [Team Member]`) + an ordered list
   of steps (approvals, notifications).
2. **Rules compile to state machines.** `TemplateCompiler` turns each rule into a
   `WorkflowDefinition` (Draft → Step1 → … → Approved, reject → Rejected → revise → Draft). Because
   the compiler is the only way to produce a definition, designers can't create broken flows — the
   diagram users see is *rendered from* the rules, not drawn by hand.
3. **Approvers resolve at submission.** N+1/N+2 are resolved against the org directory
   (`IDirectory` — swap the in-memory demo for Entra/Workday/your HR DB). The API rejects
   approve/reject calls from anyone who isn't the pending approver.

## Projects

- `src/WorkflowDemo.Core` — engine + template model + compiler. No I/O.
- `src/WorkflowDemo.Api` — minimal API + SQLite persistence; serves the built designer UI from
  `wwwroot` (generated — don't edit by hand).
- `clients/designer` — the designer UI source: **React 18 + Vite**, Mermaid for the diagram.
- `tests/WorkflowDemo.Tests` — xUnit tests for engine and compiler.

## Run

```bash
dotnet test
dotnet run --project src/WorkflowDemo.Api
```

The React app is pre-built into `wwwroot`, so Node is NOT required to run — only to change the UI:

```bash
cd clients/designer
npm install
npm run dev     # live-reload dev server, proxies /api to localhost:5000
npm run build   # rebuilds into src/WorkflowDemo.Api/wwwroot
```

Open the printed URL (e.g. `http://localhost:5000`):

- **Design rules** tab — edit a seeded workflow or create your own. The flow diagram updates live
  as you edit. Save validates by compiling every rule.
- **Run & approve** tab — pick "Act as", submit a request, then switch actors to approve.

Swagger at `/swagger`. **Note:** delete any old `workflow.db` before starting (`EnsureCreated`
doesn't migrate, and the seed data changed).

## Seeded demo workflows

Org: Alice & Evan (Team Members) → Bob (Manager) → Carol (IT Director) → Dana (CEO);
Fiona (Finance) and Hana (HR) report to Dana.

| Workflow | Rules | Demonstrates |
|---|---|---|
| Purchase Request | Team Member → N+1 then N+2; Manager/IT Director → N+1 only | the dual- vs single-approval scenario from the requirements |
| Leave Request | everyone → N+1, then auto "HR informed" | simplest flow; auto steps |
| IT Access Request | staff → N+1 then *any IT Director*; IT Director → *Dana specifically* | role-based and person-specific approvers |
| Expense Reimbursement | Team Member → N+1 then *Finance*; everyone else → N+1 | functional second-line approval |

Try: submit IT access as Alice (Bob then Carol approve), then as Carol (only Dana can approve).
Submitting a Purchase Request as Dana (CEO) shows the clean "no rule matches" error.

## API

`GET/PUT/DELETE /api/templates/{id}` (designer CRUD, PUT validates), `GET /api/directory`,
`POST /api/requests` (start), `POST /api/requests/{id}/approve|reject|resubmit`,
`GET /api/requests[/{id}]`.

## Extending

- **New step types**: add a constant in `StepTypes`, handle it in `RequestService.AutoAdvance`
  (send email, call webhook, create a task) and add it to the step-type dropdown in
  `clients/designer/src/StepRow.jsx`. Non-approval steps auto-advance.
- **New condition fields**: today conditions test requester attributes (`role`); add attributes
  (department, cost center, request amount) in `RequestService.StartAsync` and expose them in the
  designer's condition editor.
- **Parallel approvals** ("both must approve, in any order") are not modeled yet — steps are
  sequential. That needs a step group concept in the model and compiler.

## Known limits (deliberate for a demo)

- No authentication: the UI's "Act as" selector simulates identity. Put real auth in front before
  any production use and derive the actor from the token, never the request body.
- Approvers are snapshotted at submission; org changes mid-flight don't re-route.
- Editing a template does not migrate in-flight requests; they replay against the current rule and
  fail with a clear error if their rule was deleted.
- Last-write-wins concurrency on instances.
