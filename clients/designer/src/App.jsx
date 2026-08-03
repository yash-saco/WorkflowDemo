import { useCallback, useEffect, useRef, useState } from 'react';
import { api } from './api';
import DesignerTab from './DesignerTab';
import RunTab from './RunTab';

export default function App() {
  const [tab, setTab] = useState('design');
  const [employees, setEmployees] = useState([]);
  const [templates, setTemplates] = useState([]);
  const [banner, setBanner] = useState(null);
  const bannerTimer = useRef(null);

  const flash = useCallback((kind, text) => {
    clearTimeout(bannerTimer.current);
    setBanner({ kind, text });
    bannerTimer.current = setTimeout(() => setBanner(null), 5000);
  }, []);

  const refreshTemplates = useCallback(async () => {
    const list = await api('/api/templates');
    setTemplates(list);
    return list;
  }, []);

  useEffect(() => {
    (async () => {
      try {
        setEmployees(await api('/api/directory'));
        await refreshTemplates();
      } catch (e) {
        flash('error', `Failed to load: ${e.message}`);
      }
    })();
  }, [refreshTemplates, flash]);

  return (
    <>
      <header>
        <h1>Workflow Designer</h1>
        <div className="tabs">
          <button className={tab === 'design' ? 'active' : ''} onClick={() => setTab('design')}>
            Design rules
          </button>
          <button className={tab === 'run' ? 'active' : ''} onClick={() => setTab('run')}>
            Run &amp; approve
          </button>
        </div>
      </header>
      <main>
        {banner && <div className={`banner ${banner.kind}`}>{banner.text}</div>}
        {tab === 'design' ? (
          <DesignerTab
            employees={employees}
            templates={templates}
            refreshTemplates={refreshTemplates}
            flash={flash}
          />
        ) : (
          <RunTab employees={employees} templates={templates} flash={flash} />
        )}
      </main>
    </>
  );
}
