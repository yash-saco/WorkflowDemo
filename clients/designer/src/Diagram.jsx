import { useEffect, useRef } from 'react';
import mermaid from 'mermaid';

mermaid.initialize({ startOnLoad: false, theme: 'neutral' });

let seq = 0;

export default function Diagram({ text }) {
  const ref = useRef(null);

  useEffect(() => {
    let cancelled = false;
    const timer = setTimeout(async () => {
      try {
        const { svg } = await mermaid.render(`mmd${++seq}`, text);
        if (!cancelled && ref.current) ref.current.innerHTML = svg;
      } catch {
        if (!cancelled && ref.current)
          ref.current.innerHTML = '<p class="muted">Diagram cannot be rendered yet.</p>';
      }
    }, 250);
    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [text]);

  return <div className="diagram" ref={ref} />;
}
