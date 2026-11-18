'use strict';

const $ = (id) => document.getElementById(id);

function showError(msg) {
  const el = $('loginError');
  el.textContent = msg;
  el.classList.remove('d-none');
}

$('togglePw').addEventListener('click', () => {
  const pw = $('password');
  const show = pw.type === 'password';
  pw.type = show ? 'text' : 'password';
  $('togglePw').innerHTML = show ? '<i class="bi bi-eye-slash"></i>' : '<i class="bi bi-eye"></i>';
});

$('loginForm').addEventListener('submit', async (ev) => {
  ev.preventDefault();
  $('loginError').classList.add('d-none');
  const password = $('password').value;
  if (!password) return showError('กรุณากรอกรหัสผ่าน');

  const btn = $('loginBtn');
  btn.disabled = true;
  $('spin').classList.remove('d-none');
  try {
    const res = await fetch('/api/admin/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify({ password }),
    });
    if (res.ok) {
      location.replace('/admin');
      return;
    }
    let body = null;
    try { body = await res.json(); } catch { /* not json */ }
    showError(body?.message || `เข้าสู่ระบบไม่สำเร็จ (HTTP ${res.status})`);
    $('password').select();
  } catch {
    showError('ไม่สามารถติดต่อเซิร์ฟเวอร์ได้ กรุณาลองใหม่');
  } finally {
    btn.disabled = false;
    $('spin').classList.add('d-none');
  }
});
