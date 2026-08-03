export default function StepRow({
  index, step, roles, employees, isFirst, isLast, onChange, onRemove, onMove,
}) {
  const isApproval = (step.type || 'approval') === 'approval';
  const mode = step.approver?.mode || 'hierarchy';
  const level = step.approver?.level || 1;

  const approverKey =
    mode === 'hierarchy' ? `h${Math.min(level, 3)}` : mode;

  const setType = (type) =>
    onChange({
      ...step,
      type,
      approver:
        type === 'approval'
          ? step.approver ?? { mode: 'hierarchy', level: 1 }
          : null,
    });

  const setApprover = (key) => {
    if (key.startsWith('h'))
      onChange({ ...step, approver: { mode: 'hierarchy', level: parseInt(key.slice(1), 10) } });
    else if (key === 'role')
      onChange({ ...step, approver: { mode: 'role', role: roles[0] || '' } });
    else onChange({ ...step, approver: { mode: 'user', userId: employees[0]?.id || '' } });
  };

  return (
    <div className="step">
      <div className="idx">{index + 1}</div>
      <select value={isApproval ? 'approval' : 'notification'} onChange={(e) => setType(e.target.value)}>
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
      <button className="ghost" disabled={isFirst} onClick={() => onMove(-1)}>↑</button>
      <button className="ghost" disabled={isLast} onClick={() => onMove(1)}>↓</button>
      <button className="ghost danger" onClick={onRemove}>✕</button>
    </div>
  );
}
