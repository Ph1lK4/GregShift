namespace EML.App.Controls;

/// <summary>只读取已加载页面的声明式内容，返回受后续校验的数据，不返回可执行 HTML。</summary>
internal static class GtnhFlowPageDataScript
{
    internal const string Script = """
        (() => {
          const payloads = document.querySelectorAll('#mw-content-text .fcd3-payload');
          if (payloads.length !== 1) return null;
          const payload = payloads[0].textContent || '';
          if (!payload || payload.length > 2097152) return null;
          let data;
          try { data = JSON.parse(payload); } catch { return null; }
          const root = payloads[0].closest('.gtnh-flowchart-d3');
          if (!root || !data.views || typeof data.views !== 'object') return null;
          const plain = value => String(value || '').replace(/<br\s*\/?\s*>/gi, '\n').replace(/<[^>]*>/g, '').trim();
          const lines = element => {
            if (!element) return '';
            if (element.nodeType === Node.TEXT_NODE) return element.textContent || '';
            if (element.nodeName === 'BR') return '\n';
            return [...element.childNodes].map(lines).join('');
          };
          const number = (value, fallback) => Number.isFinite(Number(value)) ? Number(value) : fallback;
          function icon(element) {
            if (!element) return null;
            const img = element.querySelector('img[src]');
            const sprite = element.matches('.grid-sprite') ? element : element.querySelector('.grid-sprite');
            let address = img?.getAttribute('src');
            let frames = 1, frameSeconds = .05;
            if (sprite) {
              const match = sprite.style.backgroundImage.match(/^url\(["']?(.*?)["']?\)$/);
              address = match?.[1];
              const animation = sprite.style.animation;
              frames = number(animation.match(/steps\(\s*(\d+)/)?.[1], 1);
              const seconds = animation.match(/(?:^|\s)([\d.]+)(ms|s)(?:\s|$)/);
              if (seconds) frameSeconds = Number(seconds[1]) * (seconds[2] === 'ms' ? .001 : 1) / frames;
            } else if (img) {
              const file = img.closest('.grid-sprite-file');
              if (file) {
                frames = number(file.style.getPropertyValue('--grid-sprite-frames'), 1);
                const speed = file.style.getPropertyValue('--grid-sprite-speed').match(/^\s*([\d.]+)(ms|s)\s*$/);
                if (speed) frameSeconds = Number(speed[1]) * (speed[2] === 'ms' ? .001 : 1) / frames;
              }
            }
            if (!address) return null;
            try { address = new URL(address, location.href).href; } catch { return null; }
            return { address, frames, frameSeconds };
          }
          function recipe(source, nodeId) {
            const output = [];
            for (const entry of source.querySelectorAll('.grid-recipe')) {
              if (output.length >= 8) break;
              const board = entry.querySelector('.grid-bg-board');
              if (!board) continue;
              const slots = [];
              for (const slot of board.querySelectorAll('.grid-item-component')) {
                if (slots.length >= 128) break;
                const variants = [...slot.querySelectorAll('.nei-tt')];
                if (!variants.length) variants.push(slot);
                const items = variants.slice(0, 32).map(variant => {
                  let meta = {};
                  try { meta = JSON.parse(variant.querySelector('.nei-data[data-json]')?.getAttribute('data-json') || '{}'); } catch {}
                  const image = variant.querySelector('.recipe-item-icon') || variant;
                  const name = plain(meta.name || image.querySelector('a[title]')?.getAttribute('title') || image.querySelector('img')?.getAttribute('alt') || '未命名物品');
                  const annotation = [...variant.querySelectorAll('.item-annotation')].map(e => plain(e.textContent)).filter(Boolean).join(' · ');
                  return { name, annotation, icon: icon(image) };
                });
                slots.push({ x: number(parseFloat(slot.style.left), 0), y: number(parseFloat(slot.style.top), 0), items });
              }
              const info = entry.querySelector('.recipe-information-text');
              const information = lines(info).split('\n').map(s => s.trim()).filter(Boolean).slice(0, 32);
              output.push({ nodeId, title: plain(entry.querySelector('.grid-bg-title')?.textContent || ''),
                width: number(parseFloat(board.style.width), 348), height: number(parseFloat(board.style.height), 168), slots, information });
            }
            return output;
          }
          const media = { version: 1, views: {} };
          const allIcons = [...root.querySelectorAll('.fcd3-icon-source[data-node]')];
          for (const language of Object.keys(data.views).slice(0, 4)) {
            const view = data.views[language];
            if (!Array.isArray(view.nodes) || view.nodes.length > 2048) return null;
            const ids = new Set(view.nodes.map(n => n.id));
            const icons = [], recipes = [];
            const ownedLanguage = source => source.closest('.fcd3-language-sources')?.getAttribute('data-lang');
            for (const source of [...allIcons].sort((a, b) => Number(ownedLanguage(b) === language) - Number(ownedLanguage(a) === language))) {
              const nodeId = source.getAttribute('data-node');
              const owner = source.closest('.fcd3-language-sources');
              if (!ids.has(nodeId) || owner && owner.getAttribute('data-lang') !== language || icons.some(i => i.nodeId === nodeId)) continue;
              const image = icon(source);
              if (image) icons.push({ nodeId, icon: image });
            }
            for (const languageSource of root.querySelectorAll('.fcd3-language-sources[data-lang]')) {
              if (languageSource.getAttribute('data-lang') !== language) continue;
              for (const source of languageSource.querySelectorAll('.fcd3-tooltip-source[data-node]')) {
                const nodeId = source.getAttribute('data-node');
                if (ids.has(nodeId)) recipes.push(...recipe(source, nodeId));
              }
            }
            media.views[language] = { icons, recipes };
          }
          data.gregShiftMedia = media;
          const enriched = JSON.stringify(data);
          if (enriched.length > 2097152) return null;
          let revision = null;
          const link = document.querySelector('#t-permalink a[href]');
          if (link) {
            try {
              const value = new URL(link.getAttribute('href'), location.href).searchParams.get('oldid');
              if (value && /^\d+$/.test(value) && Number.isSafeInteger(Number(value)) && Number(value) > 0) revision = Number(value);
            } catch {}
          }
          return { PayloadJson: enriched, RevisionId: revision };
        })()
        """;
}
