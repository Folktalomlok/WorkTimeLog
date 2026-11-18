// ===== ระบบบันทึกเวลาปฏิบัติงาน — Frontend (vanilla JS + fetch) =====
'use strict';

const MAX_MINUTES = 480;
const API = '/api';

const state = {
  date: todayStr(),
  projects: [],
  entries: [],
  summary: null,
  editingId: null,
};

const $ = (id) => document.getElementById(id);

// ---------- Utilities ----------
function pad(n) { return String(n).padStart(2, '0'); }

/** วันที่ท้องถิ่นแบบ YYYY-MM-DD (ไม่ใช้ toISOString เพราะจะเป็นเวลา UTC) */
function toDateStr(d) { return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`; }
function todayStr() { return toDateStr(new Date()); }
function parseDate(s) { const [y, m, d] = s.split('-').map(Number); return new Date(y, m - 1, d); }
function addDays(s, n) { const d = parseDate(s); d.setDate(d.getDate() + n); return toDateStr(d); }

/** แสดงวันที่แบบไทย เช่น พ. 18 พ.ย. 2569 */
function thaiDate(s) {
  return parseDate(s).toLocaleDateString('th-TH', { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' });
}

/** 135 → "2 ชม. 15 นาที" */
function fmtDuration(min) {
  const h = Math.floor(min / 60), m = min % 60;
  if (h && m) return `${h} ชม. ${m} นาที`;
  if (h) return `${h} ชม.`;
  return `${m} นาที`;
}

function esc(s) {
  return String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

/** เรียก API และโยน Error พร้อมข้อความภาษาไทยจาก server ถ้าไม่สำเร็จ */
async function api(path, options = {}) {
  let res;
  try {
    res = await fetch(API + path, {
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      ...options,
    });
  } catch {
    throw new Error('ไม่สามารถติดต่อเซิร์ฟเวอร์ได้ กรุณาตรวจสอบว่าเปิดโปรแกรมฝั่งเซิร์ฟเวอร์อยู่ และเชื่อมต่อเครือข่ายเดียวกัน');
  }
  const text = await res.text();
  let data = null;
  try { data = text ? JSON.parse(text) : null; } catch { /* ไม่ใช่ JSON */ }
  if (!res.ok) {
    const msg = data?.message || data?.title || `เกิดข้อผิดพลาด (HTTP ${res.status})`;
    throw new Error(msg);
  }
  return data;
}

// ---------- Messages ----------
let toast;
function showToast(msg, type = 'success') {
  const el = $('toast');
  el.className = `toast align-items-center border-0 text-bg-${type}`;
  $('toastText').textContent = msg;
  toast ??= bootstrap.Toast.getOrCreateInstance(el, { delay: 3500 });
  toast.show();
}
function showPageError(msg) {
  $('pageErrorText').textContent = msg;
  $('pageError').classList.remove('d-none');
  window.scrollTo({ top: 0, behavior: 'smooth' });
}
function hidePageError() { $('pageError').classList.add('d-none'); }
window.hidePageError = hidePageError;

function showFormError(msg) {
  const el = $('formError');
  el.innerHTML = `<i class="bi bi-exclamation-circle-fill me-1"></i>${esc(msg)}`;
  el.classList.remove('d-none');
  el.scrollIntoView({ behavior: 'smooth', block: 'center' });
}
function hideFormError() { $('formError').classList.add('d-none'); }

// ---------- Projects ----------
async function loadProjects() {
  state.projects = await api('/projects');
  renderProjectOptions();
}

function renderProjectOptions(selectedId) {
  const sel = $('fProject');
  const current = selectedId ?? sel.value;
  const editingEntry = state.entries.find((e) => e.id === state.editingId);
  const options = state.projects
    .filter((p) => p.isActive || p.id === editingEntry?.projectId)
    .map((p) => `<option value="${p.id}">${esc(p.name)}${p.isActive ? '' : ' (ปิดใช้งาน)'}</option>`);
  sel.innerHTML = `<option value="">— เลือกโครงการ —</option>` + options.join('');
  if (current && [...sel.options].some((o) => o.value === String(current))) sel.value = String(current);
}

// ---------- Day view ----------
async function loadDay() {
  hidePageError();
  $('selectedDate').value = state.date;
  if (!state.editingId) $('fDate').value = state.date;
  try {
    const [entries, summary] = await Promise.all([
      api(`/entries?date=${state.date}`),
      api(`/summary/daily?date=${state.date}`),
    ]);
    state.entries = entries;
    state.summary = summary;
    renderUsage();
    renderEntries();
  } catch (err) {
    showPageError(err.message);
  }
}

function renderUsage() {
  const s = state.summary;
  const used = s?.totalMinutes ?? 0;
  const pct = Math.min(100, (used / MAX_MINUTES) * 100);
  const bar = $('usageBar');
  bar.style.width = `${pct}%`;
  bar.textContent = pct >= 12 ? `${Math.round(pct)}%` : '';
  // เขียว → เหลือง (≥ 75%) → แดง (≥ 90%)
  bar.className = 'progress-bar ' + (pct >= 90 ? 'bg-danger' : pct >= 75 ? 'bg-warning text-dark' : 'bg-success');
  $('usageProgress').setAttribute('aria-valuenow', used);
  $('usageText').textContent = `ใช้ไป ${used} / ${MAX_MINUTES} นาที`;
  const remain = s?.remainingMinutes ?? MAX_MINUTES;
  $('remainText').textContent = remain > 0 ? `เหลือ ${remain} นาที (${fmtDuration(remain)})` : 'เต็มแล้ว';
  $('countText').textContent = `${s?.entryCount ?? 0} รายการ` + (s?.cancelledCount ? ` · ยกเลิก ${s.cancelledCount} รายการ (ไม่นับรวม)` : '');

  const isToday = state.date === todayStr();
  $('listTitle').textContent = `รายการของ${isToday ? 'วันนี้' : 'วันที่'} — ${thaiDate(state.date)}`;
}

function actionButtons(e, small) {
  const sz = small ? '' : ' btn-sm';
  if (e.isCancelled) {
    return `<button class="btn btn-outline-success${sz}" data-action="restore" data-id="${e.id}"><i class="bi bi-arrow-counterclockwise me-1"></i>กู้คืน</button>`;
  }
  return `<button class="btn btn-outline-primary${sz}" data-action="edit" data-id="${e.id}"><i class="bi bi-pencil me-1"></i>แก้ไข</button>
          <button class="btn btn-outline-danger${sz}" data-action="cancel" data-id="${e.id}"><i class="bi bi-x-circle me-1"></i>ยกเลิก</button>`;
}

function renderEntries() {
  const list = state.entries;
  $('emptyState').classList.toggle('d-none', list.length > 0);

  // มือถือ: card
  $('entryCards').innerHTML = list.map((e) => `
    <div class="entry-card ${e.isCancelled ? 'entry-cancelled' : ''} ${e.id === state.editingId ? 'entry-editing' : ''}">
      <div class="d-flex justify-content-between gap-2">
        <div class="min-w-0">
          <span class="badge text-bg-light border mb-1">${esc(e.projectName)}</span>
          ${e.isCancelled ? '<span class="badge text-bg-secondary mb-1">ยกเลิกแล้ว</span>' : ''}
          <div class="strike text-pre">${esc(e.description)}</div>
          ${e.note ? `<div class="small text-body-secondary strike text-pre"><i class="bi bi-chat-left-text me-1"></i>${esc(e.note)}</div>` : ''}
        </div>
        <div class="text-end">
          <div class="minutes strike">${e.minutes}</div>
          <div class="small text-body-secondary">นาที</div>
        </div>
      </div>
      <div class="actions d-flex gap-2 mt-2">${actionButtons(e, true)}</div>
    </div>`).join('');

  // จอใหญ่: table
  $('entryRows').innerHTML = list.map((e) => `
    <tr class="${e.isCancelled ? 'entry-cancelled' : ''} ${e.id === state.editingId ? 'table-warning' : ''}">
      <td class="strike">${esc(e.projectName)}${e.isCancelled ? ' <span class="badge text-bg-secondary">ยกเลิกแล้ว</span>' : ''}</td>
      <td class="strike text-pre">${esc(e.description)}${e.note ? `<div class="small text-body-secondary">${esc(e.note)}</div>` : ''}</td>
      <td class="text-end fw-semibold strike">${e.minutes}</td>
      <td class="text-end text-nowrap">${actionButtons(e, false)}</td>
    </tr>`).join('');
}

// ---------- Form ----------
function startEdit(id) {
  const e = state.entries.find((x) => x.id === id);
  if (!e) return;
  state.editingId = id;
  hideFormError();
  renderProjectOptions(e.projectId);
  $('fDate').value = e.workDate;
  $('fDescription').value = e.description;
  $('fMinutes').value = e.minutes;
  $('fNote').value = e.note ?? '';
  $('formTitle').innerHTML = `<i class="bi bi-pencil-square me-1"></i>แก้ไขรายการ #${e.id}`;
  $('saveBtnText').textContent = 'บันทึกการแก้ไข';
  $('editBadge').classList.remove('d-none');
  $('cancelEditBtn').classList.remove('d-none');
  $('formCard').classList.add('editing');
  renderEntries();
  $('formCard').scrollIntoView({ behavior: 'smooth', block: 'start' });
  $('fDescription').focus({ preventScroll: true });
}

function resetForm() {
  state.editingId = null;
  hideFormError();
  $('entryForm').reset();
  $('fDate').value = state.date;
  renderProjectOptions();
  $('formTitle').innerHTML = '<i class="bi bi-plus-circle me-1"></i>เพิ่มรายการใหม่';
  $('saveBtnText').textContent = 'บันทึก';
  $('editBadge').classList.add('d-none');
  $('cancelEditBtn').classList.add('d-none');
  $('formCard').classList.remove('editing');
  renderEntries();
}

/** ตรวจเบื้องต้นฝั่งหน้าจอ (กติกาจริงตรวจที่ server เสมอ) */
function validateForm(body) {
  if (!body.workDate) return 'กรุณาระบุวันที่';
  if (!body.projectId) return 'กรุณาเลือกโครงการ';
  if (!body.description) return 'กรุณากรอกรายละเอียดงาน';
  if (body.minutes === null || Number.isNaN(body.minutes)) return 'กรุณาระบุเวลาที่ใช้ (นาที)';
  if (!Number.isInteger(body.minutes)) return 'เวลาที่ใช้ต้องเป็นจำนวนเต็ม (นาที)';
  if (body.minutes <= 0) return 'เวลาที่ใช้ต้องมากกว่า 0 นาที';
  if (body.minutes > MAX_MINUTES) return `เวลาที่ใช้ต่อรายการต้องไม่เกิน ${MAX_MINUTES} นาที`;
  return null;
}

async function onSubmit(ev) {
  ev.preventDefault();
  hideFormError();
  const minutesRaw = $('fMinutes').value.trim();
  const body = {
    workDate: $('fDate').value,
    projectId: Number($('fProject').value) || null,
    description: $('fDescription').value.trim(),
    minutes: minutesRaw === '' ? null : Number(minutesRaw),
    note: $('fNote').value.trim() || null,
  };
  const err = validateForm(body);
  if (err) return showFormError(err);

  const btn = $('saveBtn');
  btn.disabled = true;
  try {
    const editing = state.editingId;
    const saved = editing
      ? await api(`/entries/${editing}`, { method: 'PUT', body: JSON.stringify(body) })
      : await api('/entries', { method: 'POST', body: JSON.stringify(body) });
    showToast(editing ? 'บันทึกการแก้ไขแล้ว' : `เพิ่มรายการแล้ว (${saved.minutes} นาที)`);
    state.date = saved.workDate; // ไปที่วันของรายการที่บันทึก
    resetForm();
    await loadDay();
  } catch (e) {
    showFormError(e.message);
  } finally {
    btn.disabled = false;
  }
}

async function onListClick(ev) {
  const btn = ev.target.closest('button[data-action]');
  if (!btn) return;
  const id = Number(btn.dataset.id);
  const action = btn.dataset.action;
  if (action === 'edit') return startEdit(id);

  if (action === 'cancel') {
    const e = state.entries.find((x) => x.id === id);
    if (!confirm(`ยกเลิกรายการ "${e?.description ?? id}" ใช่หรือไม่?\n(สามารถกู้คืนได้ภายหลัง)`)) return;
  }
  btn.disabled = true;
  try {
    await api(`/entries/${id}/${action}`, { method: 'POST' });
    if (action === 'cancel' && state.editingId === id) resetForm();
    showToast(action === 'cancel' ? 'ยกเลิกรายการแล้ว' : 'กู้คืนรายการแล้ว');
    await loadDay();
  } catch (e) {
    showPageError(e.message);
    btn.disabled = false;
  }
}

function onQuickMinutes(ev) {
  const b = ev.target.closest('button[data-min]');
  if (!b) return;
  if (b.dataset.min === 'fill') {
    // เวลาที่เหลือของวันนี้ (ถ้ากำลังแก้ไขในวันเดียวกัน ให้คืนค่าเดิมของรายการนั้นกลับมาด้วย)
    let remain = state.summary?.remainingMinutes ?? MAX_MINUTES;
    const editing = state.entries.find((x) => x.id === state.editingId);
    if (editing && !editing.isCancelled && editing.workDate === $('fDate').value) remain += editing.minutes;
    $('fMinutes').value = Math.max(0, Math.min(MAX_MINUTES, remain)) || '';
  } else {
    $('fMinutes').value = b.dataset.min;
  }
}

// ---------- Add project ----------
async function onProjectSubmit(ev) {
  ev.preventDefault();
  const errEl = $('projectError');
  errEl.classList.add('d-none');
  const name = $('pName').value.trim();
  if (!name) {
    errEl.textContent = 'กรุณากรอกชื่อโครงการ';
    errEl.classList.remove('d-none');
    return;
  }
  try {
    const p = await api('/projects', { method: 'POST', body: JSON.stringify({ name }) });
    await loadProjects();
    renderProjectOptions(p.id);
    bootstrap.Modal.getOrCreateInstance($('projectModal')).hide();
    $('projectForm').reset();
    showToast(`เพิ่มโครงการ "${p.name}" แล้ว`);
  } catch (e) {
    errEl.textContent = e.message;
    errEl.classList.remove('d-none');
  }
}

// ---------- Summary by project ----------
function setRange(kind) {
  const today = new Date();
  let from = new Date(today);
  if (kind === 'week') {
    const dow = (today.getDay() + 6) % 7; // จันทร์ = 0
    from.setDate(today.getDate() - dow);
  } else if (kind === 'month') {
    from = new Date(today.getFullYear(), today.getMonth(), 1);
  } else if (kind === 'last30') {
    from.setDate(today.getDate() - 29);
  }
  $('sFrom').value = toDateStr(from);
  $('sTo').value = toDateStr(today);
  loadSummary();
}

async function loadSummary() {
  hidePageError();
  const from = $('sFrom').value, to = $('sTo').value;
  if (!from || !to) return showPageError('กรุณาเลือกช่วงวันที่ให้ครบ');
  try {
    const s = await api(`/summary/by-project?from=${from}&to=${to}`);
    $('summaryTitle').textContent = `สรุปเวลาแยกตามโครงการ (${thaiDate(s.from)} – ${thaiDate(s.to)})`;
    $('sTotal').textContent = s.totalMinutes ? `${s.totalMinutes} นาที` : '0 นาที';
    $('sTotal').title = fmtDuration(s.totalMinutes);
    $('sCount').textContent = s.items.reduce((a, i) => a + i.entryCount, 0);
    $('summaryEmpty').classList.toggle('d-none', s.items.length > 0);
    $('summaryList').innerHTML = s.items.map((i) => {
      const pct = s.totalMinutes ? Math.round((i.totalMinutes / s.totalMinutes) * 100) : 0;
      return `
        <div class="summary-item">
          <div class="d-flex justify-content-between gap-2">
            <span class="fw-semibold">${esc(i.projectName)}</span>
            <span class="text-nowrap fw-semibold">${i.totalMinutes} นาที</span>
          </div>
          <div class="progress my-1" role="progressbar" aria-valuenow="${pct}" aria-valuemin="0" aria-valuemax="100">
            <div class="progress-bar" style="width:${pct}%"></div>
          </div>
          <div class="d-flex justify-content-between small text-body-secondary">
            <span>${i.entryCount} รายการ · ${fmtDuration(i.totalMinutes)}</span><span>${pct}%</span>
          </div>
        </div>`;
    }).join('');
  } catch (e) {
    showPageError(e.message);
  }
}

// ---------- Init ----------
function changeDate(newDate) {
  if (!newDate) return;
  state.date = newDate;
  loadDay();
}

document.addEventListener('DOMContentLoaded', async () => {
  $('selectedDate').addEventListener('change', (e) => changeDate(e.target.value));
  $('prevDay').addEventListener('click', () => changeDate(addDays(state.date, -1)));
  $('nextDay').addEventListener('click', () => changeDate(addDays(state.date, 1)));
  $('todayBtn').addEventListener('click', () => changeDate(todayStr()));
  $('entryForm').addEventListener('submit', onSubmit);
  $('cancelEditBtn').addEventListener('click', resetForm);
  $('quickMinutes').addEventListener('click', onQuickMinutes);
  $('entryCards').addEventListener('click', onListClick);
  $('entryRows').addEventListener('click', onListClick);
  $('projectForm').addEventListener('submit', onProjectSubmit);
  $('projectModal').addEventListener('shown.bs.modal', () => $('pName').focus());
  $('summaryBtn').addEventListener('click', loadSummary);
  document.querySelectorAll('[data-range]').forEach((b) => b.addEventListener('click', () => setRange(b.dataset.range)));
  $('tab-summary-btn').addEventListener('shown.bs.tab', () => { if (!$('sFrom').value) setRange('month'); });

  try {
    await loadProjects();
  } catch (e) {
    showPageError(e.message);
  }
  await loadDay();
});
