import { useRef, useState } from 'react';

/**
 * Interactive flow editor: the diagram IS the editor.
 * The graph structure is still derived from the rules, so users can only make
 * structured edits (edit a step, insert, remove, reorder) — never an invalid flow.
 */

const newStep = () => ({
  type: 'approval',
  name: 'Approval',
  approver: { mode: 'hierarchy', level: 1 },
});

const newRule = (priority) => ({
  id: `rule-${Math.random().toString(36).slice(2, 8)}`,
  name: 'New rule',
  priority,
  condition: { field: 'role', operator: 'in', values: [] },
  steps: [newStep()],
});

function approverLabel(step, employees) {
  const a = step.approver || {};
  if (a.mode === 'role') return `any ${a.role || '?'}`;
  if (a.mode === 'user') {
    const e = employees.find((x) => x.id === a.userId);
    return e ? e.name : a.userId || '?';
  }
  return `N+${a.level || 1} manager`;
}

function conditionLabel(rule) {
  return rule.condition.operator === 'any'
    ? 'any role'
    : rule.condition.values.join(' / ') || 'nobody yet — click to pick roles';
}

function StepEditor({ step, roles, employees, onChange }) {
  const isApproval = (step.type || 'approval') === 'approval';
  const mode = step.approver?.mode || 'hierarchy';
  const level = step.approver?.level || 1;
  const approverKey = mode === 'hierarchy' ? `h${Math.min(level, 3)}` : mode;

  const setApprover = (key) => {
    if (key.startsWith('h'))
      onChange({ ...step, approver: { mode: 'hierarchy', level: parseInt(key.slice(1), 10) } });
    else if (key === 'role')
      onChange({ ...step, approver: { mode: 'role', role: roles[0] || '' } });
    else onChange({ ...step, approver: { mode: 'user', userId: employees[0]?.id || '' } });
  };

  return (
    <div className="editor-panel">
      <select
        value={isApproval ? 'approval' : 'notification'}
        onChange={(e) =>
          onChange({
            ...step,
            type: e.target.value,
            approver:
              e.target.value === 'approval'
                ? step.approver ?? { mode: 'hierarchy', level: 1 }
                : null,
          })
        }
      >
        <option value="approval">Approval</option>
        <option value="notification">Notification (auto)</option>
      </select>
      <input
        type="text"
        placeholder="Step name"
        value={step.name ?? ''}
        onChange={(e) => onChange({ ...step, name: e.target.value })}
      />
      {isApproval && (
        <select value={approverKey} onChange={(e) => setApprover(e.target.value)}>
          <option value="h1">Direct manager (N+1)</option>
          <option value="h2">Manager&#39;s manager (N+2)</option>
          <option value="h3">Three levels up (N+3)</option>
          <option value="role">Anyone with role…</option>
          <option value="user">Specific person…</option>
        </select>
      )}
      {isApproval && mode === 'role' && (
        <select
          value={step.approver?.role ?? ''}
          onChange={(e) => onChange({ ...step, approver: { mode: 'role', role: e.target.value } })}
        >
          {roles.map((r) => (
            <option key={r} value={r}>{r}</option>
          ))}
        </select>
      )}
      {isApproval && mode === 'user' && (
        <select
          value={step.approver?.userId ?? ''}
          onChange={(e) =>
            onChange({ ...step, approver: { mode: 'user', userId: e.target.value } })
          }
        >
          {employees.map((e) => (
            <option key={e.id} value={e.id}>{e.name}</option>
          ))}
        </select>
      )}
    </div>
  );
}

function ConditionEditor({ rule, roles, onChange }) {
  const isAny = rule.condition.operator === 'any';
  const toggleRole = (role, on) => {
    const values = rule.condition.values.filter((v) => v.toLowerCase() !== role.toLowerCase());
    if (on) values.push(role);
    onChange({ ...rule, condition: { ...rule.condition, operator: 'in', values } });
  };
  return (
    <div className="editor-panel roles">
      <label>
        <input
          type="radio"
          checked={isAny}
          onChange={() => onChange({ ...rule, condition: { field: 'role', operator: 'any', values: [] } })}
        />
        any role
      </label>
      <label>
        <input
          type="radio"
          checked={!isAny}
          onChange={() => onChange({ ...rule, condition: { ...rule.condition, operator: 'in' } })}
        />
        one of:
      </label>
      {!isAny &&
        roles.map((role) => (
          <label key={role}>
            <input
              type="checkbox"
              checked={rule.condition.values.some((v) => v.toLowerCase() === role.toLowerCase())}
              onChange={(e) => toggleRole(role, e.target.checked)}
            />
            {role}
          </label>
        ))}
    </div>
  );
}

export default function FlowEditor({ tpl, onChange, roles, employees }) {
  const [open, setOpen] = useState(null); // `${ri}:cond` | `${ri}:step:${si}`
  const drag = useRef(null);

  if (!tpl) return <p className="muted">No workflow selected.</p>;

  const toggle = (key) => setOpen((o) => (o === key ? null : key));
  const setRule = (i, rule) =>
    onChange({ ...tpl, rules: tpl.rules.map((r, j) => (j === i ? rule : r)) });
  const patchSteps = (ri, steps) => setRule(ri, { ...tpl.rules[ri], steps });

  const insertStep = (ri, at) => {
    const steps = [...tpl.rules[ri].steps];
    steps.splice(at, 0, newStep());
    patchSteps(ri, steps);
    setOpen(`${ri}:step:${at}`);
  };
  const removeStep = (ri, si) => {
    patchSteps(ri, tpl.rules[ri].steps.filter((_, j) => j !== si));
    setOpen(null);
  };
  const onDrop = (ri, si) => {
    const d = drag.current;
    drag.current = null;
    if (!d || d.ri !== ri || d.si === si) return;
    const steps = [...tpl.rules[ri].steps];
    const [moved] = steps.splice(d.si, 1);
    steps.splice(si, 0, moved);
    patchSteps(ri, steps);
    setOpen(null);
  };

  return (
    <div className="flow">
      <div className="flow-head">
        <span className="fnode start">Request submitted</span>
        <span className="farrow">→</span>
        <span className="fnode decision">Who is requesting?</span>
      </div>

      {tpl.rules.map((rule, ri) => (
        <div className="lane" key={rule.id}>
          <div className="lane-head">
            <input
              className="lane-name"
              type="text"
              value={rule.name}
              onChange={(e) => setRule(ri, { ...rule, name: e.target.value })}
            />
            <span className="muted">priority</span>
            <input
              type="number"
              min="1"
              style={{ width: 56 }}
              value={rule.priority}
              onChange={(e) => setRule(ri, { ...rule, priority: parseInt(e.target.value, 10) || 1 })}
            />
            <button
              className="ghost danger"
              onClick={() => {
                onChange({ ...tpl, rules: tpl.rules.filter((_, j) => j !== ri) });
                setOpen(null);
              }}
            >
              Remove rule
            </button>
          </div>

          <div className="lane-cards">
            <button
              className={`fnode cond ${open === `${ri}:cond` ? 'open' : ''}`}
              onClick={() => toggle(`${ri}:cond`)}
              title="Click to change who this branch applies to"
            >
              {conditionLabel(rule)}
            </button>
            <span className="farrow">→</span>

            {rule.steps.map((step, si) => {
              const isApproval = (step.type || 'approval') === 'approval';
              const key = `${ri}:step:${si}`;
              return (
                <span className="cardwrap" key={si}>
                  <button className="insert" title="Insert step here" onClick={() => insertStep(ri, si)}>+</button>
                  <span
                    className={`fnode step ${isApproval ? 'approval' : 'auto'} ${open === key ? 'open' : ''}`}
                    draggable
                    onDragStart={() => { drag.current = { ri, si }; }}
                    onDragOver={(e) => e.preventDefault()}
                    onDrop={() => onDrop(ri, si)}
                    onClick={() => toggle(key)}
                    title="Click to edit · drag onto another step to reorder"
                  >
                    <span className="step-name">{step.name || (isApproval ? 'Approval' : step.type)}</span>
                    <span className="step-sub">
                      {isApproval ? `✋ ${approverLabel(step, employees)}` : '⚙ automatic'}
                    </span>
                    <span
                      className="step-x"
                      title="Remove step"
                      onClick={(e) => { e.stopPropagation(); removeStep(ri, si); }}
                    >
                      ✕
                    </span>
                  </span>
                  <span className="farrow">{isApproval ? '→' : '→'}</span>
                </span>
              );
            })}

            <button className="insert" title="Add step at the end" onClick={() => insertStep(ri, rule.steps.length)}>+</button>
            <span className="fnode end">Approved</span>
          </div>

          {open === `${ri}:cond` && (
            <ConditionEditor rule={rule} roles={roles} onChange={(r) => setRule(ri, r)} />
          )}
          {open?.startsWith(`${ri}:step:`) && (() => {
            const si = parseInt(open.split(':')[2], 10);
            const step = tpl.rules[ri]?.steps[si];
            return step ? (
              <StepEditor
                step={step}
                roles={roles}
                employees={employees}
                onChange={(s) => patchSteps(ri, rule.steps.map((x, j) => (j === si ? s : x)))}
              />
            ) : null;
          })()}
        </div>
      ))}

      <button
        className="ghost"
        style={{ marginTop: 10 }}
        onClick={() => onChange({ ...tpl, rules: [...tpl.rules, newRule(tpl.rules.length + 1)] })}
      >
        + Add rule (new branch)
      </button>

      <p className="muted" style={{ marginTop: 10 }}>
        ✋ approval steps can be rejected — a rejection sends the request to <strong>Rejected</strong>,
        where the requester can revise and resubmit. Click any box to edit it; drag a step onto
        another to reorder; use <strong>+</strong> to insert steps. Changes apply when you press
        “Save workflow”.
      </p>
    </div>
  );
}
