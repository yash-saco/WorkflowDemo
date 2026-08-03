import StepRow from './StepRow';

export default function RuleCard({ rule, roles, employees, onChange, onRemove }) {
  const patch = (p) => onChange({ ...rule, ...p });

  const toggleRole = (role, on) => {
    const values = rule.condition.values.filter(
      (v) => v.toLowerCase() !== role.toLowerCase(),
    );
    if (on) values.push(role);
    patch({ condition: { ...rule.condition, values } });
  };

  const setStep = (i, step) => patch({ steps: rule.steps.map((s, j) => (j === i ? step : s)) });
  const removeStep = (i) => patch({ steps: rule.steps.filter((_, j) => j !== i) });
  const moveStep = (i, delta) => {
    const j = i + delta;
    if (j < 0 || j >= rule.steps.length) return;
    const steps = [...rule.steps];
    [steps[i], steps[j]] = [steps[j], steps[i]];
    patch({ steps });
  };
  const addStep = () =>
    patch({
      steps: [
        ...rule.steps,
        { type: 'approval', name: 'Approval', approver: { mode: 'hierarchy', level: 1 } },
      ],
    });

  const isAny = rule.condition.operator === 'any';

  return (
    <div className="rule">
      <div className="row">
        <div style={{ flex: 1 }}>
          <label>Rule name</label>
          <input
            type="text"
            style={{ width: '100%' }}
            value={rule.name}
            onChange={(e) => patch({ name: e.target.value })}
          />
        </div>
        <div>
          <label>Priority</label>
          <input
            type="number"
            style={{ width: 70 }}
            min="1"
            value={rule.priority}
            onChange={(e) => patch({ priority: parseInt(e.target.value, 10) || 1 })}
          />
        </div>
        <button className="ghost danger" onClick={onRemove}>Remove rule</button>
      </div>

      <div style={{ marginTop: 10 }}>
        <label>Applies when requester&#39;s role is…</label>
        <div className="roles">
          <label>
            <input
              type="radio"
              checked={isAny}
              onChange={() =>
                patch({ condition: { field: 'role', operator: 'any', values: [] } })
              }
            />
            any role
          </label>
          <label>
            <input
              type="radio"
              checked={!isAny}
              onChange={() => patch({ condition: { ...rule.condition, operator: 'in' } })}
            />
            one of:
          </label>
          {!isAny &&
            roles.map((role) => (
              <label key={role}>
                <input
                  type="checkbox"
                  checked={rule.condition.values.some(
                    (v) => v.toLowerCase() === role.toLowerCase(),
                  )}
                  onChange={(e) => toggleRole(role, e.target.checked)}
                />
                {role}
              </label>
            ))}
        </div>
      </div>

      <div style={{ marginTop: 10 }}>
        <label>Steps (run in order)</label>
        {rule.steps.map((step, i) => (
          <StepRow
            key={i}
            index={i}
            step={step}
            roles={roles}
            employees={employees}
            isFirst={i === 0}
            isLast={i === rule.steps.length - 1}
            onChange={(s) => setStep(i, s)}
            onRemove={() => removeStep(i)}
            onMove={(d) => moveStep(i, d)}
          />
        ))}
        <button className="ghost" style={{ marginTop: 8 }} onClick={addStep}>
          + Add step
        </button>
      </div>
    </div>
  );
}
