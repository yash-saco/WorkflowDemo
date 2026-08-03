import { useCallback, useEffect, useState } from 'react';
import { api } from './api';

export default function RunTab({ employees, templates, flash }) {
  const [actorId, setActorId] = useState('');
  const [tplId, setTplId] = useState('');
  const [title, setTitle] = useState('');
  const [requests, setRequests] = useState([]);

  useEffect(() => {
    if (employees.length && !actorId) setActorId(employees[0].id);
  }, [employees, actorId]);
  useEffect(() => {
    if (templates.length && !tplId) setTplId(templates[0].id);
  }, [templates, tplId]);

  const load = useCallback(async () => {
    try {
      setRequests(await api('/api/requests'));
    } catch (e) {
      flash('error', e.message);
    }
  }, [flash]);

  useEffect(() => {
    load();
  }, [load]);

  const actor = employees.find((e) => e.id === actorId);

  const submit = async () => {
    try {
      await api('/api/requests', {
        method: 'POST',
        body: {
          templateId: tplId,
          requesterId: actorId,
          data: { title: title || 'Untitled request' },
        },
      });
      setTitle('');
      await load();
    } catch (e) {
      flash('error', e.message);
    }
  };

  const act = async (id, action) => {
    try {
      const body = { actorId };
      if (action !== 'resubmit') body.comment = window.prompt('Optional comment:') || null;
      await api(`/api/requests/${id}/${action}`, { method: 'POST', body });
      await load();
    } catch (e) {
      flash('error', e.message);
    }
  };

  return (
    <section>
      <div className="card">
        <div className="row">
          <div>
            <label>Act as</label>
            <select value={actorId} onChange={(e) => setActorId(e.target.value)}>
              {employees.map((e) => (
                <option key={e.id} value={e.id}>
                  {e.name} — {e.role}
                </option>
              ))}
            </select>
          </div>
          <div style={{ flex: 1 }} />
          <div>
            <label>Workflow</label>
            <select value={tplId} onChange={(e) => setTplId(e.target.value)}>
              {templates.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.name}
                </option>
              ))}
            </select>
          </div>
          <div style={{ flex: 1, minWidth: 180 }}>
            <label>Request title</label>
            <input
              type="text"
              style={{ width: '100%' }}
              placeholder="e.g. New laptop"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
            />
          </div>
          <button className="primary" onClick={submit}>Submit request</button>
        </div>
      </div>

      <div className="card">
        <h2>Requests</h2>
        <table>
          <thead>
            <tr>
              <th>Request</th><th>Requester</th><th>Status</th><th>Waiting on</th><th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {requests.length === 0 && (
              <tr><td colSpan="5" className="muted">No requests yet.</td></tr>
            )}
            {requests.map((r) => {
              const mine = r.requester?.id === actor?.id;
              const canAct =
                r.pendingApprover &&
                ((r.pendingApprover.id && r.pendingApprover.id === actor?.id) ||
                  (r.pendingApprover.role &&
                    r.pendingApprover.role.toLowerCase() === (actor?.role || '').toLowerCase())) &&
                !mine;
              const waiting = r.pendingApprover
                ? r.pendingApprover.name || `anyone: ${r.pendingApprover.role}`
                : '—';
              const notes = Object.entries(r.data || {})
                .filter(([k]) => /^step\d+:(decision|note)$/.test(k))
                .map(([, v]) => v);
              return (
                <tr key={r.id}>
                  <td>
                    <strong>{r.data?.title || '(untitled)'}</strong>
                    <div className="muted">{r.templateId} · {r.ruleId}</div>
                    {notes.map((n, i) => (
                      <div className="muted" key={i}>· {n}</div>
                    ))}
                    <details>
                      <summary className="muted">history</summary>
                      <ul className="hist">
                        {(r.history || []).map((h, i) => (
                          <li key={i}>
                            {h.fromState} → {h.toState} ({h.trigger})
                          </li>
                        ))}
                      </ul>
                    </details>
                  </td>
                  <td>
                    {r.requester?.name || '?'}
                    <div className="muted">{r.requester?.role || ''}</div>
                  </td>
                  <td>
                    <span className={`state ${r.state}`}>{r.state}</span>
                    <div className="muted">{r.stepName || ''}</div>
                  </td>
                  <td>{waiting}</td>
                  <td>
                    {canAct && (
                      <>
                        <button className="ok" onClick={() => act(r.id, 'approve')}>Approve</button>{' '}
                        <button className="rej" onClick={() => act(r.id, 'reject')}>Reject</button>
                      </>
                    )}
                    {mine && r.state === 'Rejected' && (
                      <button className="ghost" onClick={() => act(r.id, 'resubmit')}>
                        Resubmit
                      </button>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </section>
  );
}
