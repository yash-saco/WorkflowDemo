import { useCallback, useEffect, useMemo, useState } from 'react';
import { api } from './api';
import FlowEditor from './FlowEditor';

export default function DesignerTab({ employees, templates, refreshTemplates, flash }) {
  const [tpl, setTpl] = useState(null);

  const loadTemplate = useCallback(
    async (id) => {
      try {
        setTpl(await api(`/api/templates/${encodeURIComponent(id)}`));
      } catch (e) {
        flash('error', e.message);
      }
    },
    [flash],
  );

  useEffect(() => {
    if (!tpl && templates.length) loadTemplate(templates[0].id);
  }, [templates, tpl, loadTemplate]);

  const roles = useMemo(() => {
    const set = new Set(employees.map((e) => e.role));
    (tpl?.rules ?? []).forEach((r) => (r.condition?.values ?? []).forEach((v) => set.add(v)));
    return [...set];
  }, [employees, tpl]);

  const create = () => {
    const name = prompt('Name of the new workflow (e.g. "Access Request"):');
    if (!name) return;
    const id = name.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/(^-|-$)/g, '');
    if (!id) return flash('error', 'The name must contain at least one letter or number.');
    if (templates.some((t) => t.id === id))
      return flash('error', `A workflow with id "${id}" already exists — pick a different name.`);
    setTpl({
      id,
      name,
      rules: [
        {
          id: `rule-${Math.random().toString(36).slice(2, 8)}`,
          name: 'New rule',
          priority: 1,
          condition: { field: 'role', operator: 'in', values: [] },
          steps: [
            { type: 'approval', name: 'Manager approval', approver: { mode: 'hierarchy', level: 1 } },
          ],
        },
      ],
    });
    flash('ok', 'New workflow started — edit the flow, then press "Save workflow".');
  };

  const save = async () => {
    if (!tpl) return;
    const empty = tpl.rules.find(
      (r) => r.condition.operator !== 'any' && (r.condition.values || []).length === 0,
    );
    if (empty)
      return flash(
        'error',
        `Rule "${empty.name}" has no roles ticked — it would never match anyone.`,
      );
    try {
      await api(`/api/templates/${encodeURIComponent(tpl.id)}`, { method: 'PUT', body: tpl });
      flash('ok', 'Saved. This flow is now live.');
      await refreshTemplates();
    } catch (e) {
      flash('error', e.message);
    }
  };

  const remove = async () => {
    if (!tpl || !window.confirm(`Delete workflow "${tpl.name}"?`)) return;
    try {
      await api(`/api/templates/${encodeURIComponent(tpl.id)}`, { method: 'DELETE' });
      setTpl(null);
      await refreshTemplates();
    } catch (e) {
      flash('error', e.message);
    }
  };

  return (
    <section>
      <div className="card">
        <div className="row">
          <div>
            <label>Workflow</label>
            <select value={tpl?.id ?? ''} onChange={(e) => loadTemplate(e.target.value)}>
              {!templates.some((t) => t.id === tpl?.id) && tpl && (
                <option value={tpl.id}>{tpl.name} (unsaved)</option>
              )}
              {templates.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.name}
                </option>
              ))}
            </select>
          </div>
          <button className="ghost" onClick={create}>+ New workflow</button>
          <div style={{ flex: 1 }} />
          <button className="ghost danger" onClick={remove}>Delete</button>
          <button className="primary" onClick={save}>Save workflow</button>
        </div>
        <div className="row" style={{ marginTop: 10 }}>
          <div style={{ flex: 1 }}>
            <label>Workflow name</label>
            <input
              type="text"
              style={{ width: '100%' }}
              value={tpl?.name ?? ''}
              onChange={(e) => setTpl((t) => (t ? { ...t, name: e.target.value } : t))}
            />
          </div>
        </div>
      </div>

      <div className="card">
        <h2>Flow (click any box to edit)</h2>
        <p className="muted" style={{ margin: '0 0 10px' }}>
          Rules are checked top to bottom; the first branch matching the requester runs.
        </p>
        <FlowEditor tpl={tpl} onChange={setTpl} roles={roles} employees={employees} />
      </div>
    </section>
  );
}
