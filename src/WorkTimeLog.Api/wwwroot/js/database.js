// ===== หน้า "ดูฐานข้อมูล" (อ่านอย่างเดียว) =====
'use strict';

const MAX_MINUTES = 480;
const $ = (id) => document.getElementById(id);
const data = { info: null, projects: [], entries: [], daily: [] };

function esc(s) {
  return String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}
function parseDate(s) { const [y, m, d] = s.split('-').map(Number); return new Date(y, m - 1, d); }
function thaiDate(s) {
  return parseDate(s).toLocaleDateString('th-TH', { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' });
}
function thaiDateTime(iso) {
  if (!iso) return '';
  // เวลาจาก server เป็น UTC — ถ้าไม่มี timezone ต่อท้ายให้ถือเป็น UTC
  const d = new Date(/[zZ]|[+-]\d\d:?\d\d$/.test(iso) ? iso : iso + 'Z');
  return d.toLocaleString('th-TH', { day: 'numeric', month: 'short', year: '2-digit', hour: '2-digit', minute: '2-digit' });
}
function fmtDuration(min) {
  const h = Math.floor(min / 60), m = min % 60;
  if (h && m) return `${h} ชม. ${m} นาที`;
  if (h) return `${h} ชม.`;
  return `${m} นาที`;
}
function fmtBytes(n) {
  if (n < 1024) return `${n} B`;
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`;
  return `${(n / 1024 / 1024).toFixed(2)} MB`;
}
const num = (n) => Number(n).toLocaleString('th-TH');

async function api(path) {
  let res;
  try {
    res = await fetch('api/database/' + path, { headers: { Accept: 'application/json' } });
  } catch {
    throw new Error('ไม่สามารถติดต่อเซิร์ฟเวอร์ได้ กรุณาตรวจสอบว่าเซิร์ฟเวอร์ยังทำงานอยู่');
  }
  const text = await res.text();
  let body = null;
  try { body = text ? JSON.parse(text) : null; } catch { /* not json */ }
  if (!res.ok) throw new Error(body?.message || `เกิดข้อผิดพลาด (HTTP ${res.status})`);
  return body;
}

function showError(msg) {
  $('pageErrorText').textContent = msg;
  $('pageError').classList.remove('d-none');
}
function hideError() { $('pageError').classList.add('d-none'); }

// ---------- render ----------
function renderInfo() {
  const i = data.info;
  $('cType').textContent = i.databaseType;
  $('cVersion').textContent = i.engineVersion ? `เวอร์ชัน ${i.engineVersion}` : '';
  $('cProjects').textContent = num(i.projectCount);
  $('cEntries').textContent = num(i.entryCount);
  $('cActive').textContent = `ปกติ ${num(i.activeEntryCount)} รายการ`;
  $('cMinutes').textContent = `${num(i.totalMinutes)} นาที`;
  $('cHours').textContent = fmtDuration(i.totalMinutes) + ' (ไม่นับที่ยกเลิก)';
  $('cCancelled').textContent = num(i.cancelledCount);
  $('cPath').textContent = i.filePath;
  $('cSize').textContent = fmtBytes(i.fileSizeBytes) + (i.walSizeBytes ? ` (+ WAL ${fmtBytes(i.walSizeBytes)})` : '');
  $('cTables').textContent = i.tables.map((t) => `${t.name} = ${num(t.rows)}`).join(', ');
  $('updatedAt').textContent = `อัปเดตล่าสุด ${new Date().toLocaleTimeString('th-TH')}`;
}

function renderProjects() {
  $('bProjects').textContent = data.projects.length;
  $('projectRows').innerHTML = data.projects.length
    ? data.projects.map((p) => `
      <tr class="${p.isActive ? '' : 'entry-cancelled'}">
        <td>${p.id}</td>
        <td class="text-wrap">${esc(p.name)}</td>
        <td>${p.isActive ? '<span class="badge text-bg-success">ใช้งาน</span>' : '<span class="badge text-bg-secondary">ปิดใช้งาน</span>'}</td>
        <td class="text-end">${num(p.entryCount)}</td>
        <td class="text-end fw-semibold">${num(p.totalMinutes)}</td>
      </tr>`).join('')
    : '<tr><td colspan="5" class="text-center text-body-secondary py-4">ไม่มีข้อมูล</td></tr>';
}

function filteredEntries() {
  const q = $('fSearch').value.trim().toLowerCase();
  const from = $('fFrom').value, to = $('fTo').value, status = $('fStatus').value;
  return data.entries.filter((e) => {
    if (from && e.workDate < from) return false;
    if (to && e.workDate > to) return false;
    if (status === 'active' && e.isCancelled) return false;
    if (status === 'cancelled' && !e.isCancelled) return false;
    if (q) {
      const hay = `${e.id} ${e.projectName} ${e.description} ${e.note ?? ''}`.toLowerCase();
      if (!hay.includes(q)) return false;
    }
    return true;
  });
}

function renderEntries() {
  const list = filteredEntries();
  $('bEntries').textContent = data.entries.length;
  const minutes = list.filter((e) => !e.isCancelled).reduce((a, e) => a + e.minutes, 0);
  $('filterInfo').textContent =
    `แสดง ${num(list.length)} จาก ${num(data.entries.length)} รายการ · เวลารวม (ไม่นับที่ยกเลิก) ${num(minutes)} นาที`;
  $('entryRows').innerHTML = list.length
    ? list.map((e) => `
      <tr class="${e.isCancelled ? 'entry-cancelled' : ''}">
        <td>${e.id}</td>
        <td class="text-nowrap" title="${esc(e.workDate)}">${esc(e.workDate)}</td>
        <td class="text-nowrap strike">${esc(e.projectName)}</td>
        <td class="strike text-pre">${esc(e.description)}</td>
        <td class="text-end fw-semibold strike">${e.minutes}</td>
        <td class="small text-pre">${esc(e.note ?? '')}</td>
        <td>${e.isCancelled ? '<span class="badge text-bg-secondary">ยกเลิก</span>' : '<span class="badge text-bg-success">ปกติ</span>'}</td>
        <td class="small text-nowrap">${thaiDateTime(e.createdAt)}</td>
        <td class="small text-nowrap">${thaiDateTime(e.updatedAt)}</td>
      </tr>`).join('')
    : '<tr><td colspan="9" class="text-center text-body-secondary py-4">ไม่พบรายการตามเงื่อนไข</td></tr>';
}

function renderDaily() {
  $('bDaily').textContent = data.daily.length;
  $('dailyRows').innerHTML = data.daily.length
    ? data.daily.map((d) => {
      const pct = Math.min(100, Math.round((d.totalMinutes / MAX_MINUTES) * 100));
      const color = d.isOverLimit || pct >= 90 ? 'bg-danger' : pct >= 75 ? 'bg-warning' : 'bg-success';
      return `
      <tr class="${d.isOverLimit ? 'table-danger' : ''}">
        <td>${esc(d.date)}<div class="small text-body-secondary">${thaiDate(d.date)}</div></td>
        <td class="text-end fw-semibold ${d.isOverLimit ? 'text-danger' : ''}">${num(d.totalMinutes)}</td>
        <td class="text-end">${num(d.remainingMinutes)}</td>
        <td>
          <div class="progress" style="height:.6rem" role="progressbar" aria-valuenow="${pct}" aria-valuemin="0" aria-valuemax="100">
            <div class="progress-bar ${color}" style="width:${pct}%"></div>
          </div>
          <div class="small text-body-secondary">${pct}%</div>
        </td>
        <td class="text-end">${d.entryCount}</td>
        <td class="text-end">${d.cancelledCount}</td>
        <td>${d.isOverLimit
          ? '<span class="badge text-bg-danger">เกิน 480</span>'
          : d.totalMinutes === MAX_MINUTES
            ? '<span class="badge text-bg-warning">เต็มพอดี</span>'
            : '<span class="badge text-bg-success">ไม่เกิน</span>'}</td>
      </tr>`;
    }).join('')
    : '<tr><td colspan="7" class="text-center text-body-secondary py-4">ไม่มีข้อมูล</td></tr>';
}

async function loadAll() {
  hideError();
  const btn = $('refreshBtn');
  btn.disabled = true;
  try {
    const [info, projects, entries, daily] = await Promise.all([
      api('info'), api('projects'), api('entries'), api('daily-totals'),
    ]);
    Object.assign(data, { info, projects, entries, daily });
    renderInfo();
    renderProjects();
    renderEntries();
    renderDaily();
  } catch (e) {
    showError(e.message);
  } finally {
    btn.disabled = false;
  }
}

document.addEventListener('DOMContentLoaded', () => {
  $('refreshBtn').addEventListener('click', loadAll);
  ['fSearch', 'fFrom', 'fTo', 'fStatus'].forEach((id) => $(id).addEventListener('input', renderEntries));
  $('clearFilter').addEventListener('click', () => {
    $('fSearch').value = ''; $('fFrom').value = ''; $('fTo').value = ''; $('fStatus').value = 'all';
    renderEntries();
  });
  loadAll();
});
