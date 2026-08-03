const mlabel = (s) => String(s ?? '').replace(/"/g, "'");

function approverText(step, employees) {
  const a = step.approver || {};
  if (a.mode === 'role') return `role: ${a.role || '?'}`;
  if (a.mode === 'user') {
    const e = employees.find((x) => x.id === a.userId);
    return e ? e.name : a.userId || '?';
  }
  return `N+${a.level || 1}`;
}

/** Builds the mermaid source for a template. Rendered read-only: users edit rules, not the graph. */
export function diagramText(tpl, employees) {
  if (!tpl || !tpl.rules.length) return 'flowchart TD\n  A[No rules yet]';
  const lines = [
    'flowchart TD',
    '  S([Request submitted]) --> C{Who is requesting?}',
    '  A((Approved))',
    '  X((Rejected))',
    '  X -. "revise / resubmit" .-> S',
  ];
  const sorted = [...tpl.rules].sort((a, b) => a.priority - b.priority);
  sorted.forEach((r, ri) => {
    const cond =
      r.condition.operator === 'any' ? 'any role' : r.condition.values.join(' / ') || '—';
    let prev = 'C';
    let edge = `-->|"${mlabel(cond)}"|`;
    r.steps.forEach((s, si) => {
      const id = `R${ri}S${si}`;
      const isApproval = (s.type || 'approval') === 'approval';
      const label = isApproval
        ? `${mlabel(s.name || 'Approval')}<br/>(${mlabel(approverText(s, employees))})`
        : `${mlabel(s.name || s.type)} (auto)`;
      lines.push(isApproval ? `  ${id}["${label}"]` : `  ${id}(["${label}"])`);
      lines.push(`  ${prev} ${edge} ${id}`);
      if (isApproval) lines.push(`  ${id} -. reject .-> X`);
      prev = id;
      edge = isApproval ? '-- approve -->' : '-->';
    });
    lines.push(`  ${prev} ${edge} A`);
  });
  return lines.join('\n');
}
